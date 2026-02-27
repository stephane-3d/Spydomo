using Microsoft.EntityFrameworkCore;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public class StrategicSummaryRepository : IStrategicSummaryRepository
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public StrategicSummaryRepository(IDbContextFactory<SpydomoContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<StrategicSummary?> GetLatestForGroupAsync(int groupId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.StrategicSummaries
                .AsNoTracking()
                .Where(s => s.CompanyGroupId == groupId)
                .OrderByDescending(s => s.CreatedOn)
                .FirstOrDefaultAsync(ct);
        }

        public async Task AddAsync(StrategicSummary summary, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            db.StrategicSummaries.Add(summary);
            await db.SaveChangesAsync(ct);
        }

        public async Task<List<StrategicSummary>> GetSummariesForGroupAsync(int groupId, int days = 30, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var cutoff = DateTime.UtcNow.AddDays(-days);

            return await db.StrategicSummaries
                .AsNoTracking()
                .Where(s => s.CompanyGroupId == groupId && s.CreatedOn >= cutoff)
                .OrderByDescending(s => s.CreatedOn)
                .ToListAsync(ct);
        }
    }
}
