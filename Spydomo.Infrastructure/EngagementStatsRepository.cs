using Microsoft.EntityFrameworkCore;
using Spydomo.Common;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure
{
    public sealed class EngagementStatsRepository : IEngagementStatsRepository
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        public EngagementStatsRepository(IDbContextFactory<SpydomoContext> dbFactory) => _dbFactory = dbFactory;

        public async Task<double> GetBaselineAsync(
            int companyId,
            int? sourceTypeId,
            DateTime nowUtc,
            string periodType = "30d",
            CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var end = nowUtc.Date;
            var days = periodType switch
            {
                "30d" => 30,
                "90d" => 90,
                _ => throw new NotSupportedException($"Unsupported periodType: {periodType}")
            };
            var start = end.AddDays(-days);

            const int maxValues = 1500;

            // 1) Per-source (if provided)
            if (sourceTypeId.HasValue && sourceTypeId.Value > 0)
            {
                var values = await db.SummarizedInfos
                    .AsNoTracking()
                    .Where(si => si.CompanyId == companyId
                              && si.OriginType != OriginTypeEnum.UserGenerated
                              && si.Date != null
                              && si.Date >= start && si.Date < end
                              && si.SourceTypeId == sourceTypeId.Value
                              && si.RawContentId != null)
                    .OrderByDescending(si => si.Date)
                    .Select(si => (double)(si.RawContent!.EngagementScore))
                    .Take(maxValues)
                    .ToListAsync(ct);

                if (values.Count > 0)
                {
                    var med = Median(values);
                    if (med > 0) return med;

                    var avg = values.Average();
                    if (avg > 0) return avg;
                }
            }

            // 2) Fallback: company-wide average
            var fallback = await db.SummarizedInfos
                .AsNoTracking()
                .Where(si => si.CompanyId == companyId
                          && si.OriginType != OriginTypeEnum.UserGenerated
                          && si.Date != null
                          && si.Date >= start && si.Date < end
                          && si.RawContentId != null)
                .OrderByDescending(si => si.Date)
                .Select(si => (double)(si.RawContent!.EngagementScore))
                .Take(maxValues)
                .ToListAsync(ct);

            if (fallback.Count == 0) return 0;

            var avgFallback = fallback.Average();
            return avgFallback > 0 ? avgFallback : 0;

            static double Median(List<double> values)
            {
                values.Sort();
                int n = values.Count;
                if (n == 0) return 0;
                if (n % 2 == 1) return values[n / 2];
                return (values[(n / 2) - 1] + values[n / 2]) / 2.0;
            }
        }
    }
}
