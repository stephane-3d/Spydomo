using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IStrategicSummaryRepository
    {
        Task<StrategicSummary?> GetLatestForGroupAsync(int groupId, CancellationToken ct = default);
        Task AddAsync(StrategicSummary summary, CancellationToken ct = default);
        Task<List<StrategicSummary>> GetSummariesForGroupAsync(int groupId, int days = 30, CancellationToken ct = default);
    }
}
