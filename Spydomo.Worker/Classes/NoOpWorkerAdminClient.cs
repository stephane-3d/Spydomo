using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Worker.Classes
{
    public class NoOpWorkerAdminClient : IWorkerAdminClient
    {
        private readonly ILogger<NoOpWorkerAdminClient> _logger;

        public NoOpWorkerAdminClient(ILogger<NoOpWorkerAdminClient> logger)
            => _logger = logger;

        private Task<string> Skip(string op)
        {
            _logger.LogDebug("NoOpWorkerAdminClient: skipping {Op} (worker host)", op);
            return Task.FromResult("SKIPPED");
        }

        public Task<string> EnqueueWarmupAsync(int clientId, int companyId, CancellationToken ct = default)
            => Skip($"EnqueueWarmup clientId={clientId} companyId={companyId}");

        public Task<string> EnqueueCompanyDataAsync(int companyId, CancellationToken ct = default)
            => Skip($"EnqueueCompanyData companyId={companyId}");

        public Task<string> ProcessCompanyDataAsync(int companyId, bool inline = true, CancellationToken ct = default)
            => Skip($"ProcessCompanyDataAsync companyId={companyId}");

        public Task<string> ProcessInternalContentAsync(int companyId, bool inline = true, CancellationToken ct = default)
            => Skip($"ProcessInternalContentAsync companyId={companyId}");

        public Task<string> ProcessFeedbackAsync(int companyId, bool inline = true, bool force = false, CancellationToken ct = default)
            => Skip($"ProcessFeedbackAsync companyId={companyId}");

        public Task<string> ProcessStrategicSummariesAsync(int companyId, bool inline = true, CancellationToken ct = default)
            => Skip($"ProcessStrategicSummariesAsync companyId={companyId}");

        public Task<string> FetchRedditMentionsAsync(int companyId, bool inline = true, CancellationToken ct = default)
            => Skip($"FetchRedditMentionsAsync companyId={companyId}");
    }

}
