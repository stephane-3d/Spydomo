using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class StrategicSummaryJobService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly StrategicSummaryService _summaryService;
        private readonly ILogger<StrategicSummaryJobService> _logger;

        private const int BatchSize = 50;
        private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(10);
        private readonly ICompanyGroupStrategicSummaryStateStore _stateStore;

        public StrategicSummaryJobService(
            IDbContextFactory<SpydomoContext> dbFactory,
            StrategicSummaryService summaryService,
            ILogger<StrategicSummaryJobService> logger, ICompanyGroupStrategicSummaryStateStore stateStore)
        {
            _dbFactory = dbFactory;
            _summaryService = summaryService;
            _logger = logger;
            _stateStore = stateStore;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 60 * 10)]
        public async Task RunAsync(IJobCancellationToken hangfireToken)
        {
            _logger.LogInformation("⏱ StrategicSummaryJobService > RunAsync started");

            var ct = hangfireToken?.ShutdownToken ?? CancellationToken.None;

            try
            {
                var now = DateTimeOffset.UtcNow;

                // 1) Compute group candidates with their "latest summarized info id"
                List<(int groupId, int maxSummaryId)> candidates;
                await using (var db = await _dbFactory.CreateDbContextAsync(ct))
                {
                    candidates = await (
                        from tcg in db.TrackedCompanyGroups.AsNoTracking()
                        join tc in db.TrackedCompanies.AsNoTracking() on tcg.TrackedCompanyId equals tc.Id
                        join si in db.SummarizedInfos.AsNoTracking() on tc.CompanyId equals si.CompanyId
                        where si.ProcessingStatus >= SummarizedInfoProcessingStatus.GistReady
                            && si.ProcessingStatus != SummarizedInfoProcessingStatus.Error
                        group si by tcg.CompanyGroupId into g
                        select new { GroupId = g.Key, MaxSummaryId = g.Max(x => x.Id) }
                    )
                    .OrderBy(x => x.GroupId)
                    .Take(BatchSize * 3) // pull more, we’ll filter by watermark + locking next
                    .Select(x => new ValueTuple<int, int>(x.GroupId, x.MaxSummaryId))
                    .ToListAsync(ct);
                }

                if (candidates.Count == 0)
                {
                    _logger.LogInformation("No candidate groups found for strategic summary.");
                    return;
                }
                else
                {
                    _logger.LogInformation("Found {Count} candidate groups for strategic summary.", candidates.Count);
                }

                // 2) Process groups that actually need refresh (watermark check + lock)
                var processed = 0;

                foreach (var (groupId, maxSummaryId) in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    if (processed >= BatchSize) break;

                    // Acquire lock + watermark check in DB (so multiple workers won't duplicate)
                    var acquired = await TryAcquireGroupAsync(groupId, maxSummaryId, now, ct);
                    if (!acquired) continue;

                    try
                    {
                        _logger.LogInformation(
                            "Strategic summary: processing GroupId={GroupId} up to SummarizedInfoId={MaxId}",
                            groupId, maxSummaryId);

                        // IMPORTANT: use the watermark from DB, not maxSummaryId
                        var lastProcessedId = await _stateStore.GetLastProcessedIdAsync(groupId, ct);

                        var (summaries, maxProcessedId) =
                            await _summaryService.GenerateSummaryForGroupAsync(groupId, lastProcessedId, "daily", ct);

                        if (summaries.Count == 0)
                        {
                            // Nothing new; release lock and move on
                            await ReleaseGroupLockAsync(groupId, ct);
                            continue;
                        }

                        // Save (dedupe by SourceKey) — call local SaveAsync
                        var inserted = await SaveAsync(summaries, ct);

                        // ✅ advance watermark ONLY if we actually inserted something
                        if (inserted > 0 && maxProcessedId > lastProcessedId)
                            await _stateStore.SetLastProcessedIdAsync(groupId, maxProcessedId, ct);
                        else
                            await ReleaseGroupLockAsync(groupId, ct);

                        processed++;

                        _logger.LogInformation("✅ Strategic summary done for GroupId={GroupId}", groupId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Strategic summary failed for GroupId={GroupId}", groupId);
                        await ReleaseGroupLockAsync(groupId, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Strategic summary job cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Strategic summary job failed.");
                throw;
            }

            _logger.LogInformation("✅ StrategicSummaryJobService > RunAsync completed");
        }

        private async Task<bool> TryAcquireGroupAsync(int groupId, int maxSummaryId, DateTimeOffset now, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Ensure state row exists
            var state = await db.CompanyGroupStrategicSummaryStates
                .FirstOrDefaultAsync(x => x.CompanyGroupId == groupId, ct);

            if (state is null)
            {
                state = new CompanyGroupStrategicSummaryState
                {
                    CompanyGroupId = groupId,
                    LastProcessedSummarizedInfoId = 0
                };
                db.CompanyGroupStrategicSummaryStates.Add(state);
                await db.SaveChangesAsync(ct);
            }

            // Watermark check
            if (maxSummaryId <= state.LastProcessedSummarizedInfoId)
                return false;

            // Lock check
            if (state.LockedUntilUtc.HasValue && state.LockedUntilUtc.Value > now)
                return false;

            // Acquire lock
            state.LockedUntilUtc = now.Add(LockTtl);
            await db.SaveChangesAsync(ct);

            return true;
        }

        private async Task LogStatusBreakdownAsync(int groupId, int lastProcessedId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var breakdown = await (
                from tcg in db.TrackedCompanyGroups.AsNoTracking()
                join tc in db.TrackedCompanies.AsNoTracking() on tcg.TrackedCompanyId equals tc.Id
                join si in db.SummarizedInfos.AsNoTracking() on tc.CompanyId equals si.CompanyId
                where tcg.CompanyGroupId == groupId
                   && si.Id > lastProcessedId
                group si by si.ProcessingStatus into g
                select new { Status = g.Key, Count = g.Count() }
            ).ToListAsync(ct);

            _logger.LogInformation("GroupId={GroupId} SI status breakdown after watermark {Watermark}: {Breakdown}",
                groupId,
                lastProcessedId,
                string.Join(", ", breakdown.Select(x => $"{x.Status}={x.Count}")));
        }

        private async Task<bool> TryAcquireGroupLockAsync(int groupId, int maxSummaryId, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var state = await db.CompanyGroupStrategicSummaryStates
                .FirstOrDefaultAsync(x => x.CompanyGroupId == groupId, ct);

            if (state is null)
            {
                state = new CompanyGroupStrategicSummaryState { CompanyGroupId = groupId, LastProcessedSummarizedInfoId = 0 };
                db.CompanyGroupStrategicSummaryStates.Add(state);
                await db.SaveChangesAsync(ct);
            }

            if (maxSummaryId <= state.LastProcessedSummarizedInfoId)
                return false;

            if (state.LockedUntilUtc is not null && state.LockedUntilUtc > now)
                return false;

            state.LockedUntilUtc = now.AddMinutes(10);
            await db.SaveChangesAsync(ct);

            return true;
        }


        private async Task ReleaseGroupLockAsync(int groupId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            await db.CompanyGroupStrategicSummaryStates
                .Where(x => x.CompanyGroupId == groupId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null), ct);
        }

        // Optional: your admin endpoint can call this for one company
        public async Task RunForCompanyAsync(int companyId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var groupIds = await db.TrackedCompanyGroups
                .AsNoTracking()
                .Where(x => x.TrackedCompany.CompanyId == companyId)
                .Select(x => x.CompanyGroupId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var groupId in groupIds)
            {
                ct.ThrowIfCancellationRequested();

                // ✅ Now includes GistReady+ (and excludes Error)
                var maxSummaryId = await GetMaxEligibleSiIdForGroupAsync(groupId, ct);
                if (maxSummaryId <= 0) continue;

                if (!await TryAcquireGroupLockAsync(groupId, maxSummaryId, ct))
                    continue;

                try
                {
                    var lastProcessedId = await _stateStore.GetLastProcessedIdAsync(groupId, ct);

                    // ✅ New helper log: status breakdown for SIs after watermark
                    await LogStatusBreakdownAsync(groupId, lastProcessedId, ct);

                    var (summaries, maxProcessedId) =
                        await _summaryService.GenerateSummaryForGroupAsync(groupId, lastProcessedId, "daily", ct);

                    if (summaries.Count > 0)
                        await SaveAsync(summaries, ct);

                    if (maxProcessedId > lastProcessedId)
                        await _stateStore.SetLastProcessedIdAsync(groupId, maxProcessedId, ct);
                    else
                        await ReleaseGroupLockAsync(groupId, ct);
                }
                catch
                {
                    await ReleaseGroupLockAsync(groupId, ct);
                    throw;
                }
            }
        }

        private async Task<int> GetMaxEligibleSiIdForGroupAsync(int groupId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var maxId = await (
                from tcg in db.TrackedCompanyGroups.AsNoTracking()
                join tc in db.TrackedCompanies.AsNoTracking() on tcg.TrackedCompanyId equals tc.Id
                join si in db.SummarizedInfos.AsNoTracking() on tc.CompanyId equals si.CompanyId
                where tcg.CompanyGroupId == groupId
                   && si.ProcessingStatus >= SummarizedInfoProcessingStatus.GistReady
                   && si.ProcessingStatus != SummarizedInfoProcessingStatus.Error
                select (int?)si.Id
            ).MaxAsync(ct);

            return maxId ?? 0;
        }

        public async Task<int> SaveAsync(List<StrategicSummary> rows, CancellationToken ct)
        {
            if (rows == null || rows.Count == 0) return 0;

            var groupId = rows[0].CompanyGroupId;
            var periodType = rows[0].PeriodType;

            var keys = rows
                .Select(x => x.SourceKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .ToList();

            // 🚨 If SourceKey is missing, treat as "can't save"
            if (keys.Count == 0)
            {
                _logger.LogWarning("StrategicSummary SaveAsync: all SourceKey are empty. groupId={GroupId} rows={Rows}", groupId, rows.Count);
                return 0;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var existingKeys = await db.StrategicSummaries
                .AsNoTracking()
                .Where(x => x.CompanyGroupId == groupId
                         && x.PeriodType == periodType
                         && x.SourceKey != null
                         && keys.Contains(x.SourceKey))
                .Select(x => x.SourceKey!)
                .ToListAsync(ct);

            var existing = existingKeys.ToHashSet(StringComparer.Ordinal);

            var toInsert = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.SourceKey) && !existing.Contains(x.SourceKey!))
                .ToList();

            if (toInsert.Count == 0) return 0;

            db.StrategicSummaries.AddRange(toInsert);
            await db.SaveChangesAsync(ct);
            return toInsert.Count;
        }
    }
}
