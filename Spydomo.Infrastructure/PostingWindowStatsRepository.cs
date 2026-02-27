using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public sealed class PostingWindowStatsRepository : IPostingWindowStatsRepository
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public PostingWindowStatsRepository(IDbContextFactory<SpydomoContext> dbFactory)
            => _dbFactory = dbFactory;

        public async Task<PostingWindowStats?> GetAsync(int companyId, string periodType, CancellationToken ct = default)
        {
            var end = DateTime.UtcNow.Date;

            var days = periodType switch
            {
                "30d" => 30,
                "90d" => 90,
                _ => throw new NotSupportedException($"Unsupported periodType: {periodType}")
            };

            var start = end.AddDays(-days);
            var prevEnd = start;
            var prevStart = prevEnd.AddDays(-days);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // 1) current + previous counts in one query
            var counts = await db.SummarizedInfos
                .AsNoTracking()
                .Where(si => si.CompanyId == companyId
                          && si.OriginType != OriginTypeEnum.UserGenerated
                          && si.Date != null
                          && (
                                (si.Date >= start && si.Date < end) ||
                                (si.Date >= prevStart && si.Date < prevEnd)
                             ))
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Curr = g.Count(x => x.Date >= start && x.Date < end),
                    Prev = g.Count(x => x.Date >= prevStart && x.Date < prevEnd)
                })
                .FirstOrDefaultAsync(ct);

            if (counts is null)
                return new PostingWindowStats(start, end, 0, 0, new());

            // 2) source breakdown for current window (company-authored only)
            var breakdownRows = await db.SummarizedInfos
                .AsNoTracking()
                .Where(si => si.CompanyId == companyId
                          && si.OriginType != OriginTypeEnum.UserGenerated
                          && si.Date != null
                          && si.Date >= start && si.Date < end
                          && si.SourceTypeId != null)
                .GroupBy(si => si.SourceTypeId!.Value)
                .Select(g => new { SourceTypeId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var breakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in breakdownRows)
            {
                var key = Enum.IsDefined(typeof(DataSourceTypeEnum), row.SourceTypeId)
                    ? ((DataSourceTypeEnum)row.SourceTypeId).ToString().ToLowerInvariant()
                    : $"src_{row.SourceTypeId}";

                breakdown[key] = row.Count;
            }

            return new PostingWindowStats(start, end, counts.Curr, counts.Prev, breakdown);
        }
    }
}
