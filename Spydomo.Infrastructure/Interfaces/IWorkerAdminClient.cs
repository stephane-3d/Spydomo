namespace Spydomo.Infrastructure.Interfaces
{
    public interface IWorkerAdminClient
    {
        Task<string> EnqueueWarmupAsync(int clientId, int companyId, CancellationToken ct = default);
        Task<string> EnqueueCompanyDataAsync(int companyId, CancellationToken ct = default);

        Task<string> ProcessCompanyDataAsync(int companyId, bool inline = true, CancellationToken ct = default);
        Task<string> ProcessInternalContentAsync(int companyId, bool inline = true, CancellationToken ct = default);
        Task<string> ProcessFeedbackAsync(int companyId, bool inline = true, bool force = false, CancellationToken ct = default);
        Task<string> ProcessStrategicSummariesAsync(int companyId, bool inline = true, CancellationToken ct = default);
        Task<string> FetchRedditMentionsAsync(int companyId, bool inline = true, CancellationToken ct = default);

    }
}
