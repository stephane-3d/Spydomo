using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.Interfaces;
using System.Net.Http.Headers;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public sealed class EmbeddingBackfillJobService
    {
        private readonly ILogger<EmbeddingBackfillJobService> _log;
        private readonly ISemanticSignalRepository _repo;
        private readonly IConfiguration _cfg;
        private readonly IHttpClientFactory _httpFactory;

        public EmbeddingBackfillJobService(
            ILogger<EmbeddingBackfillJobService> log,
            ISemanticSignalRepository repo,
            IConfiguration cfg,
            IHttpClientFactory httpFactory)
        {
            _log = log;
            _repo = repo;
            _cfg = cfg;
            _httpFactory = httpFactory;
        }

        public async Task RunAsync(IJobCancellationToken hangfireToken)
        {
            var ct = hangfireToken?.ShutdownToken ?? CancellationToken.None;

            // Feature flag (fast exit)
            if (!bool.TryParse(_cfg["Features:UseEmbeddings"], out var useEmb) || !useEmb)
                return;

            var apiKey = _cfg["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _log.LogWarning("Embeddings enabled but OpenAI:ApiKey is missing.");
                return;
            }

            var http = _httpFactory.CreateClient("openai");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var batch = await _repo.QueryForEmbeddingBackfillAsync(200, ct);
            if (batch.Count == 0)
                return;

            foreach (var row in batch)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // TODO: decide canonical text later
                    var text = $"company:{row.CompanyId} source:{row.SourceType} intents:{row.IntentsJson} keywords:{row.KeywordsJson}";
                    var embedding = await GetEmbeddingAsync(http, text, ct);

                    await _repo.UpdateEmbeddingAsync(row.Hash, embedding, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Embedding backfill failed for Hash={Hash}", row.Hash);
                }
            }
        }

        private static async Task<byte[]> GetEmbeddingAsync(HttpClient http, string text, CancellationToken ct)
        {
            // Placeholder; implement your embeddings call later
            await Task.Yield();
            return Array.Empty<byte>();
        }
    }
}
