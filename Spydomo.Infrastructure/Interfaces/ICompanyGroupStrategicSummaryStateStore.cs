namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompanyGroupStrategicSummaryStateStore
    {
        Task<int> GetLastProcessedIdAsync(int groupId, CancellationToken ct);
        Task SetLastProcessedIdAsync(int groupId, int lastProcessedId, CancellationToken ct);

        // optional for the Hangfire batch worker
        Task<bool> TryAcquireLockAsync(int groupId, DateTimeOffset now, TimeSpan ttl, CancellationToken ct);
        Task ReleaseLockAsync(int groupId, CancellationToken ct);
        Task MarkRunAsync(int groupId, DateTimeOffset now, CancellationToken ct);
    }
}
