namespace Spydomo.Infrastructure.Interfaces
{
    public interface IStrategicSummaryStateStore
    {
        Task<int> GetLastProcessedIdAsync(int groupId, CancellationToken ct);
        Task SetLastProcessedIdAsync(int groupId, int lastProcessedId, CancellationToken ct);
    }

}
