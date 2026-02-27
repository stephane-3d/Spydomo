using Microsoft.Extensions.Configuration;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Utilities;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public sealed class PerplexityCompanyLandscapeClient : IPerplexityCompanyLandscapeClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public PerplexityCompanyLandscapeClient(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public async Task<CompanyLandscapeResponse> GetLandscapeAsync(
            string companyName,
            string companyUrl,
            int limit = 10,
            int? companyId = null,
            CancellationToken ct = default)
        {
            var apiKey = _config["Perplexity:ApiKey"];
            var model = _config["Perplexity:Model"] ?? "sonar";

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Missing Perplexity:ApiKey");

            if (string.IsNullOrWhiteSpace(companyName))
                throw new ArgumentException("companyName is required.", nameof(companyName));

            if (string.IsNullOrWhiteSpace(companyUrl))
                throw new ArgumentException("companyUrl is required.", nameof(companyUrl));

            var user = $@"
You are doing competitive landscape research on {companyName} ({companyUrl}).

IMPORTANT - WHAT COUNTS AS PROVIDED CONTENT
- The provided content is ONLY the public web content you can access by visiting {companyUrl} and obvious product pages linked from it
  (e.g., /product, /features, /pricing, /use-cases, /solutions).
- Do NOT rely on prior knowledge. Use only what you can read from those pages.
- You MUST read at least 2 pages if available (homepage + one product/pricing/features page). If not available, proceed with 1 page and lower confidence.

TASK
Return about {limit} companies that are competitors, alternatives, or in the same space.

RULES
- Output MUST be JSON only.
- Each item must include:
  - name
  - url (official product/company website, not a directory like G2/Capterra/LinkedIn/Wikipedia)
  - relationType in [Competitor, Alternative, SameSpace, Adjacent]
  - confidence 0..1
  - reason (<= 20 words)
  - evidence: 1–2 exact phrases copied verbatim from the pages you read (for why this company belongs in the same space)
- If you cannot find strong evidence, lower confidence and still return best effort.

CONSTRAINTS
- URLs must be official domains whenever possible.
- Avoid returning duplicates or near-duplicates.
";

            var responseFormat = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "company_landscape",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            companies = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    properties = new
                                    {
                                        name = new { type = "string" },
                                        url = new { type = "string" },
                                        relationType = new { type = "string" },
                                        confidence = new { type = "number" },
                                        reason = new { type = "string" },
                                        evidence = new { type = "array", items = new { type = "string" } }
                                    },
                                    required = new[] { "name", "url", "relationType", "confidence", "reason", "evidence" }
                                }
                            }
                        },
                        required = new[] { "companies" }
                    }
                }
            };

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var req = new
            {
                model,
                messages = new object[] { new { role = "user", content = user } },
                response_format = responseFormat,
                temperature = 0.1
            };

            var resp = await _httpClient.PostAsJsonAsync("https://api.perplexity.ai/chat/completions", req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Perplexity API call failed: {raw}");

            using var doc = JsonDocument.Parse(raw);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var clean = JsonHelper.StripJsonCodeBlock(content ?? "");

            await _usageLogger.LogAsync(doc.RootElement, "CompanyLandscape (Perplexity)", companyId, user);

            try
            {
                return JsonSerializer.Deserialize<CompanyLandscapeResponse>(
                    clean,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                )!;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to parse CompanyLandscape JSON:\n" + (content ?? ""), ex);
            }
        }
    }
}
