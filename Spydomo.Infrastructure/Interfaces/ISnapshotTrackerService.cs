using Spydomo.Common.Enums;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISnapshotTrackerService
    {
        Task TrackAsync(string snapshotId, int companyId, int dataSourceTypeId, string trackingData, string? dateFilter = null,
            OriginTypeEnum originType = OriginTypeEnum.UserGenerated, CancellationToken ct = default);

        Task TrackUrlJobAsync(string snapshotId, int companyId, int dataSourceTypeId, CancellationToken ct = default);
        Task<int?> GetCompanyIdAsync(string snapshotId, CancellationToken ct = default);

        Task MarkSnapshotCompletedAsync(string snapshotId, CancellationToken ct = default);

        Task<SnapshotJob?> GetJobBySnapshotIdAsync(string snapshotId, CancellationToken ct = default);

        Task MarkSnapshotFailedAsync(string snapshotId, CancellationToken ct = default);

        Task MarkSnapshotCompletedWithWarningsAsync(string snapshotId, CancellationToken ct = default);
    }
}
