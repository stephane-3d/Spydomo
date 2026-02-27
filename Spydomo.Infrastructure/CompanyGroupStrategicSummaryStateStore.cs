using Microsoft.EntityFrameworkCore;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public class CompanyGroupStrategicSummaryStateStore : ICompanyGroupStrategicSummaryStateStore
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public CompanyGroupStrategicSummaryStateStore(IDbContextFactory<SpydomoContext> dbFactory)
            => _dbFactory = dbFactory;

        public async Task<int> GetLastProcessedIdAsync(int groupId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var state = await db.CompanyGroupStrategicSummaryStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyGroupId == groupId, ct);

            if (state != null)
                return state.LastProcessedSummarizedInfoId;

            // Create row if missing (safe for first run)
            db.CompanyGroupStrategicSummaryStates.Add(new CompanyGroupStrategicSummaryState
            {
                CompanyGroupId = groupId,
                LastProcessedSummarizedInfoId = 0,
                LastRunUtc = null,
                LockedUntilUtc = null
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // In case two threads created it at once; ignore and re-read
            }

            // Re-read
            return await db.CompanyGroupStrategicSummaryStates
                .AsNoTracking()
                .Where(x => x.CompanyGroupId == groupId)
                .Select(x => x.LastProcessedSummarizedInfoId)
                .FirstAsync(ct);
        }

        public async Task SetLastProcessedIdAsync(int groupId, int lastProcessedId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Ensure row exists (cheap)
            var exists = await db.CompanyGroupStrategicSummaryStates
                .AsNoTracking()
                .AnyAsync(x => x.CompanyGroupId == groupId, ct);

            if (!exists)
            {
                db.CompanyGroupStrategicSummaryStates.Add(new CompanyGroupStrategicSummaryState
                {
                    CompanyGroupId = groupId,
                    LastProcessedSummarizedInfoId = lastProcessedId,
                    LastRunUtc = DateTimeOffset.UtcNow,
                    LockedUntilUtc = null
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                    return;
                }
                catch (DbUpdateException)
                {
                    // Someone else inserted; fall through to update
                }
            }

            await db.CompanyGroupStrategicSummaryStates
                .Where(x => x.CompanyGroupId == groupId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LastProcessedSummarizedInfoId, lastProcessedId)
                    .SetProperty(x => x.LastRunUtc, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null), ct);
        }

        public async Task<bool> TryAcquireLockAsync(int groupId, DateTimeOffset now, TimeSpan ttl, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Ensure exists
            var lastProcessed = await GetLastProcessedIdAsync(groupId, ct); // creates if missing

            // Try lock: only lock if (null or expired)
            var updated = await db.CompanyGroupStrategicSummaryStates
                .Where(x => x.CompanyGroupId == groupId &&
                            (x.LockedUntilUtc == null || x.LockedUntilUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LockedUntilUtc, now.Add(ttl)), ct);

            return updated == 1;
        }

        public async Task ReleaseLockAsync(int groupId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            await db.CompanyGroupStrategicSummaryStates
                .Where(x => x.CompanyGroupId == groupId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null), ct);
        }

        public async Task MarkRunAsync(int groupId, DateTimeOffset now, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            await db.CompanyGroupStrategicSummaryStates
                .Where(x => x.CompanyGroupId == groupId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LastRunUtc, now), ct);
        }
    }
}
