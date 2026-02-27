using Microsoft.Extensions.Options;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure.Clients
{
    public class WorkerAdminClient : IWorkerAdminClient
    {
        private readonly HttpClient _http;
        private readonly WorkerAdminOptions _opt;

        public WorkerAdminClient(HttpClient http, IOptions<WorkerAdminOptions> opt)
        {
            _http = http;
            _opt = opt.Value;
        }

        private async Task<string> PostAsync(string url, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("X-Admin-Key", _opt.AdminApiKey);

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} - Body: {body}");

            return string.IsNullOrWhiteSpace(body) ? "OK" : body;
        }

        public Task<string> ProcessCompanyDataAsync(int companyId, bool inline = true, CancellationToken ct = default)
        {
            var mode = inline ? "inline" : "hangfire";
            return PostAsync($"/api/admin/jobs/companydata/process?companyId={companyId}&mode={mode}", ct);
        }

        public Task<string> ProcessInternalContentAsync(int companyId, bool inline = true, CancellationToken ct = default)
        {
            var mode = inline ? "inline" : "hangfire";
            return PostAsync($"/api/admin/jobs/internalcontent/process?companyId={companyId}&mode={mode}", ct);
        }

        public Task<string> ProcessFeedbackAsync(int companyId, bool inline = true, bool force = false, CancellationToken ct = default)
        {
            var mode = inline ? "inline" : "hangfire";
            var forceStr = force ? "true" : "false";
            return PostAsync($"/api/admin/jobs/feedback/process?companyId={companyId}&mode={mode}&force={forceStr}", ct);
        }

        public Task<string> ProcessStrategicSummariesAsync(int companyId, bool inline = true, CancellationToken ct = default)
        {
            var mode = inline ? "inline" : "hangfire";
            return PostAsync($"/api/admin/jobs/strategicsummaries/process?companyId={companyId}&mode={mode}", ct);
        }

        public Task<string> FetchRedditMentionsAsync(int companyId, bool inline = true, CancellationToken ct = default)
        {
            var mode = inline ? "inline" : "hangfire";
            return PostAsync($"/api/admin/jobs/reddit/fetch?companyId={companyId}&mode={mode}", ct);
        }

        public Task<string> EnqueueWarmupAsync(int clientId, int companyId, CancellationToken ct = default)
            => PostAsync($"/api/admin/jobs/warmup/generate?clientId={clientId}&companyId={companyId}&mode=hangfire", ct);

        public Task<string> EnqueueCompanyDataAsync(int companyId, CancellationToken ct = default)
            => PostAsync($"/api/admin/jobs/companydata/process?companyId={companyId}&mode=hangfire", ct);


    }


}
