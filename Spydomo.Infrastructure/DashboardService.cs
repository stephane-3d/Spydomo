using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public class DashboardService : IDashboardService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IMemoryCache _cache;

        public DashboardService(IDbContextFactory<SpydomoContext> dbFactory, IMemoryCache cache)
        {
            _dbFactory = dbFactory;
            _cache = cache;
        }

        public async Task<List<StrategicSignalDto>> GetSignalsAsync(SignalQueryParams query, int clientId)
        {
            // Include all params that can affect results
            var key =
                $"dash:signals:{clientId}:" +
                $"g:{query.GroupId?.ToString() ?? "all"}:" +
                $"d:{query.PeriodDays}:" +
                $"c:{query.Company ?? ""}:" +
                $"src:{query.Source ?? ""}:" +
                $"th:{query.Theme ?? ""}:" +
                $"st:{query.SignalType ?? ""}:" +
                $"imp:{query.Importance ?? ""}";

            if (_cache is null)
                return await FetchSignalsAsync(query, clientId);

            return await _cache.GetOrCreateAsync(key, async entry =>
            {
                var results = await FetchSignalsAsync(query, clientId);

                if (results.Count == 0)
                {
                    // ✅ Don't poison cache when warmup is about to appear
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
                    entry.SlidingExpiration = TimeSpan.FromSeconds(5);
                }
                else
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                    entry.SlidingExpiration = TimeSpan.FromMinutes(1);
                }

                return results;
            }) ?? new List<StrategicSignalDto>();
        }

        private async Task<List<StrategicSignalDto>> FetchSignalsAsync(SignalQueryParams query, int clientId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            // companies tracked by this client
            var companyIdsForClient = await db.TrackedCompanies
                .AsNoTracking()
                .Where(tc => tc.ClientId == clientId)
                .Select(tc => tc.CompanyId)
                .Distinct()
                .ToListAsync();

            if (companyIdsForClient.Count == 0)
                return new List<StrategicSignalDto>();

            // ✅ allowed groups for this client (all or a specific one)
            var allowedGroupIdsQ = db.CompanyGroups
                .AsNoTracking()
                .Where(g => g.ClientId == clientId);

            if (query.GroupId.HasValue)
                allowedGroupIdsQ = allowedGroupIdsQ.Where(g => g.Id == query.GroupId.Value);

            var allowedGroupIds = await allowedGroupIdsQ
                .Select(g => g.Id)
                .ToListAsync();

            if (allowedGroupIds.Count == 0)
                return new List<StrategicSignalDto>();

            var cutoffDate = DateTime.UtcNow.AddDays(-query.PeriodDays);

            var q = db.StrategicSummaries
                .AsNoTracking()
                .Include(s => s.Company)
                .Where(s => s.CreatedOn >= cutoffDate);

            // ✅ prevent cross-client / cross-group duplicates
            q = q.Where(s => allowedGroupIds.Contains(s.CompanyGroupId));

            // ✅ only companies tracked by this client
            q = q.Where(s => s.CompanyId.HasValue && companyIdsForClient.Contains(s.CompanyId.Value));

            // ✅ recommended default: dashboard uses daily summaries
            q = q.Where(s => s.PeriodType == "daily" || s.PeriodType == "warmup");

            // Optional future filters (only if you start populating these meaningfully)
            if (!string.IsNullOrWhiteSpace(query.Company))
                q = q.Where(s => s.Company != null && s.Company.Name.Contains(query.Company));

            if (!string.IsNullOrWhiteSpace(query.SignalType))
                q = q.Where(s => s.IncludedSignalTypes.Any(t => t.ToString() == query.SignalType));

            if (!string.IsNullOrWhiteSpace(query.Importance))
            {
                // Example mapping if Importance corresponds to Tier; adjust to your rules
                // q = q.Where(s => s.Tier != null && s.Tier.ToString() == query.Importance);
            }

            var raw = await q
                .OrderByDescending(s => s.CreatedOn)
                .Take(100)
                .ToListAsync(ct);

            // SummarizedInfo lookups
            var siIds = raw.Where(x => x.SummarizedInfoId.HasValue)
                           .Select(x => x.SummarizedInfoId!.Value)
                           .Distinct()
                           .ToList();

            Dictionary<int, List<string>> tagsBySiId = new();
            Dictionary<int, List<string>> themesBySiId = new();

            if (siIds.Count > 0)
            {
                var infos = await db.SummarizedInfos
                    .AsNoTracking()
                    .Where(i => siIds.Contains(i.Id))
                    .Select(i => new
                    {
                        i.Id,
                        Tags = i.SummarizedInfoTags.Select(t => t.CanonicalTag.Name), // adjust
                        Themes = i.SummarizedInfoThemes.Select(t => t.CanonicalTheme.Name) // adjust
                    })
                    .ToListAsync(ct);

                tagsBySiId = infos.ToDictionary(
                    x => x.Id,
                    x => x.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList()
                );

                themesBySiId = infos.ToDictionary(
                    x => x.Id,
                    x => x.Themes.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList()
                );
            }

            return raw.Select(s =>
            {
                var tags = s.SummarizedInfoId is int sid && tagsBySiId.TryGetValue(sid, out var t) ? t : new List<string>();
                var themes = s.SummarizedInfoId is int sid2 && themesBySiId.TryGetValue(sid2, out var th) ? th : new List<string>();

                return new StrategicSignalDto
                {
                    Id = s.Id,
                    CompanyId = s.CompanyId ?? 0,
                    CompanyName = s.Company?.Name ?? "(Unknown)",
                    Gist = s.SummaryText,
                    Tags = tags,
                    ThemeList = themes,
                    SourceType = s.SummarizedInfoId.HasValue ? "UserGenerated" : "CompanyMove",
                    Types = s.IncludedSignalTypes?.Select(t => t.ToString()).ToList() ?? new(),
                    Tier = s.Tier,
                    TierReason = s.TierReason,
                    Url = s.Url,
                    CreatedOn = s.CreatedOn,
                    PeriodType = s.PeriodType
                };
            }).ToList();
        }

    }
}