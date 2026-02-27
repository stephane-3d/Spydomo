using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public class SnapshotTrackerService : ISnapshotTrackerService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public SnapshotTrackerService(IDbContextFactory<SpydomoContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task TrackAsync(string snapshotId, int companyId, int dataSourceTypeId, string keyword, string dateFilter,
            OriginTypeEnum originType = OriginTypeEnum.UserGenerated,
            CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var exists = await db.SnapshotJobs
                .AnyAsync(j => j.SnapshotId == snapshotId);

            if (exists)
            {
                Console.WriteLine($"Snapshot ID {snapshotId} already tracked — skipping insert.");
                return;
            }

            db.SnapshotJobs.Add(new SnapshotJob
            {
                SnapshotId = snapshotId,
                CompanyId = companyId,
                DataSourceTypeId = dataSourceTypeId,
                TrackingData = keyword,
                DateFilter = dateFilter,
                TriggeredAt = DateTime.UtcNow,
                Status = "Pending",
                OriginType = originType
            });

            await db.SaveChangesAsync();
        }

        public async Task TrackUrlJobAsync(string snapshotId, int companyId, int dataSourceTypeId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var exists = await db.SnapshotJobs
                .AnyAsync(j => j.SnapshotId == snapshotId);

            if (exists)
            {
                Console.WriteLine($"📌 Snapshot ID {snapshotId} already tracked — skipping insert.");
                return;
            }

            db.SnapshotJobs.Add(new SnapshotJob
            {
                SnapshotId = snapshotId,
                CompanyId = companyId,
                DataSourceTypeId = dataSourceTypeId,
                TriggeredAt = DateTime.UtcNow,
                Status = "Pending"
            });

            await db.SaveChangesAsync();
        }

        public async Task MarkSnapshotCompletedAsync(string snapshotId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var job = await db.SnapshotJobs
                .FirstOrDefaultAsync(j => j.SnapshotId == snapshotId);

            if (job != null)
            {
                job.CompletedAt = DateTime.UtcNow;
                job.Status = "Completed";
                await db.SaveChangesAsync();
            }
        }

        public async Task MarkSnapshotFailedAsync(string snapshotId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var job = await db.SnapshotJobs
                .FirstOrDefaultAsync(j => j.SnapshotId == snapshotId);

            if (job != null)
            {
                job.CompletedAt = DateTime.UtcNow;
                job.Status = "Failed";
                await db.SaveChangesAsync();
            }
        }

        public async Task<int?> GetCompanyIdAsync(string snapshotId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var job = await db.SnapshotJobs.FirstOrDefaultAsync(j => j.SnapshotId == snapshotId);
            return job?.CompanyId;
        }

        public async Task<SnapshotJob?> GetJobBySnapshotIdAsync(string snapshotId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.SnapshotJobs
                .FirstOrDefaultAsync(j =>
                    j.SnapshotId == snapshotId &&
                    j.Status == "Pending");
        }

        public async Task MarkSnapshotCompletedWithWarningsAsync(string snapshotId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var job = await db.SnapshotJobs
                .FirstOrDefaultAsync(j => j.SnapshotId == snapshotId, ct);

            if (job != null)
            {
                job.CompletedAt = DateTime.UtcNow;
                job.Status = "CompletedWithWarnings";
                await db.SaveChangesAsync(ct);
            }
        }


    }
}
