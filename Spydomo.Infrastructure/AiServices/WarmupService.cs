using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Constants;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spydomo.Infrastructure.AiServices
{
    public class WarmupService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<WarmupService> _logger;
        private readonly IAiUsageLogger _usageLogger;

        public WarmupService(
            IDbContextFactory<SpydomoContext> dbFactory,
            IHttpClientFactory httpFactory,
            IConfiguration config,
            ILogger<WarmupService> logger,
            IAiUsageLogger usageLogger)
        {
            _dbFactory = dbFactory;
            _httpClient = httpFactory.CreateClient("Readability");
            _config = config;
            _logger = logger;
            _usageLogger = usageLogger;
        }

        // ✅ Hangfire entrypoint (no optional args)
        public Task GenerateWarmupHangfireAsync(int clientId, int companyId)
            => GenerateWarmupAsync(clientId, companyId, CancellationToken.None);

        public async Task GenerateWarmupAsync(int clientId, int companyId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            _logger.LogInformation("🔥 Warmup started for: clientId={ClientId} companyId={CompanyId}", clientId, companyId);

            // 0) Resolve company + default group
            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
            if (company == null) return;

            var defaultSlug = $"default-{clientId}";
            var defaultGroupId = await db.CompanyGroups
                .Where(g => g.ClientId == clientId && g.Slug == defaultSlug)
                .Select(g => g.Id)
                .SingleAsync(ct);

            // 1) Skip if we already have real signals for this company/group (last N days)
            var cutoff = DateTime.UtcNow.AddDays(-14);

            var alreadyHasReal = await db.StrategicSummaries.AsNoTracking()
                .AnyAsync(s =>
                    s.CompanyGroupId == defaultGroupId &&
                    s.CompanyId == companyId &&
                    s.CreatedOn >= cutoff &&
                    (s.SourceKey == null || !EF.Functions.Like(s.SourceKey, "warmup:%")),
                    ct);

            if (alreadyHasReal)
            {
                _logger.LogInformation("Warmup skipped (real signals exist): clientId={ClientId} companyId={CompanyId}", clientId, companyId);
                return;
            }

            // 2) Skip if warmup exists recently
            var alreadyHasWarmup = await db.StrategicSummaries.AsNoTracking()
                .AnyAsync(s =>
                    s.CompanyGroupId == defaultGroupId &&
                    s.CompanyId == companyId &&
                    s.CreatedOn >= cutoff &&
                    s.SourceKey != null &&
                    EF.Functions.Like(s.SourceKey, "warmup:perplexity:%"),
                    ct);

            if (alreadyHasWarmup)
            {
                _logger.LogInformation("Warmup skipped (warmup already exists): clientId={ClientId} companyId={CompanyId}", clientId, companyId);
                return;
            }

            // 3) Call Perplexity
            var prompt = BuildWarmupPrompt(company);

            var items = await CallPerplexityAsync(prompt, companyId, ct);
            if (items == null || items.Count == 0)
            {
                _logger.LogWarning("Warmup returned no items: clientId={ClientId} companyId={CompanyId}", clientId, companyId);
                return;
            }

            // 4) Write StrategicSummaries (Tier + TierReason are perfect for this)
            var now = DateTime.UtcNow;

            foreach (var item in items.Take(3))
            {
                var sourceKey = BuildSourceKey(companyId, item);

                // dedupe by SourceKey
                var exists = await db.StrategicSummaries.AsNoTracking()
                    .AnyAsync(s => s.CompanyGroupId == defaultGroupId && s.SourceKey == sourceKey, ct);
                if (exists) continue;

                var summaryText = item.Point.Trim();

                var row = new StrategicSummary
                {
                    CompanyGroupId = defaultGroupId,
                    CompanyId = companyId,
                    PeriodType = "warmup",
                    SourceKey = sourceKey,
                    SummaryText = summaryText,
                    Url = item.EvidenceUrl ?? "",
                    CreatedOn = now,
                    Tier = PulseTier.Tier2,
                    TierReason = item.Reason.Trim(),
                    IncludedSignalTypes = new List<string> { SignalSlugs.DiscoverySignal }
                };

                db.StrategicSummaries.Add(row);
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Warmup created: clientId={ClientId} companyId={CompanyId} count={Count}",
                clientId, companyId, items.Count);
        }

        private static string BuildSourceKey(int companyId, WarmupPoint item)
        {
            // stable-ish: hash the point + evidence_url
            var raw = $"{companyId}|{item.Point}|{item.EvidenceUrl}";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))
                .Substring(0, 12)
                .ToLowerInvariant();

            return $"warmup:perplexity:v1:{hash}";
        }

        private string BuildWarmupPrompt(Company company)
        {
            // keep it short + strict JSON to avoid flaky parsing
            return @$"
You are a competitive intelligence analyst for SaaS.
Focus on marketing/product signals: positioning, messaging, packaging/pricing, integrations, platform direction, release momentum.

Company:
- Name: {company.Name}
- Website: {company.Url}

Task:
Return EXACTLY 3 items about {company.Name}. Each item must be non-obvious and strategically significant.

Constraints:
- point: exactly ONE sentence, <= 24 words
- reason: one sentence, why it matters
- competitive_implication: one sentence, what a competitor should do/position
- Avoid exaggeration; if uncertain say 'some users' not 'users'
- Ground points in concrete benefit/pain/change
- Prefer official + neutral sources; minimize competitor blog posts. If you use competitor_content, mark it.
- Include citations as URLs.

Return ONLY valid JSON (no markdown, no commentary) in this format:
[
  {{
    ""point"": ""..."",
    ""reason"": ""..."",
    ""competitive_implication"": ""..."",
    ""confidence"": 0.0,
    ""evidence_quote"": ""..."",
    ""evidence_url"": ""https://..."",
    ""source_urls"": [""https://...""],
    ""source_types"": [""official|news|directory|review|competitor_content""],
    ""recency_note"": ""...""
  }}
]
";
        }

        private async Task<List<WarmupPoint>> CallPerplexityAsync(string prompt, int companyId, CancellationToken ct)
        {
            var apiKey = _config["Perplexity:ApiKey"];
            var model = _config["Perplexity:Model"] ?? "sonar"; // adjust to your chosen model

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Missing Perplexity:ApiKey");

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            // Perplexity API shape may differ depending on endpoint/model; adjust if needed.
            var requestBody = new
            {
                model,
                messages = new[]
                {
                new { role = "user", content = prompt }
            },
                temperature = 0.2
            };

            var resp = await _httpClient.PostAsJsonAsync("https://api.perplexity.ai/chat/completions", requestBody, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Perplexity API call failed: {raw}");

            using var doc = JsonDocument.Parse(raw);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            var cleaned = JsonHelper.StripJsonCodeBlock(content ?? "");
            var parsed = JsonSerializer.Deserialize<List<WarmupPoint>>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // log usage (raw json)
            await _usageLogger.LogAsync(doc.RootElement, "WarmupPerplexity", companyId, prompt);

            return parsed ?? new List<WarmupPoint>();
        }

        private sealed class WarmupPoint
        {
            public string Point { get; set; } = "";
            public string Reason { get; set; } = "";
            [JsonPropertyName("competitive_implication")]
            public string CompetitiveImplication { get; set; } = "";
            public double Confidence { get; set; }
            [JsonPropertyName("evidence_quote")]
            public string EvidenceQuote { get; set; } = "";
            [JsonPropertyName("evidence_url")]
            public string EvidenceUrl { get; set; } = "";
            public List<string> SourceUrls { get; set; } = new();
            public List<string> SourceTypes { get; set; } = new();
            [JsonPropertyName("recency_note")]
            public string RecencyNote { get; set; } = "";
        }
    }

}
