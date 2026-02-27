namespace Spydomo.Infrastructure.Interfaces
{
    // Infrastructure/Interfaces/IPulseObservationRepository.cs
    public interface IPulseObservationRepository
    {
        Task UpsertTodayAsync(int companyId, string type, string topicKey, DateTime nowUtc, CancellationToken ct = default);
        Task<bool> ExistsTodayAsync(int companyId, string type, string topicKey, DateOnly today, CancellationToken ct = default);
        Task<DateTime?> GetLastNotifiedAtAsync(int companyId, string type, string topicKey, CancellationToken ct = default);
        Task SetLastNotifiedAtAsync(int companyId, string type, string topicKey, DateTime whenUtc, CancellationToken ct = default);
        Task<int> CountSinceAsync(int companyId, string type, string topicKey, DateTime sinceUtc, CancellationToken ct = default);
    }

}
