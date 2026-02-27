using Microsoft.Extensions.Configuration;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Utilities;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public class PerplexityKeywordExtractor : IKeywordExtractor
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public PerplexityKeywordExtractor(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public async Task<KeywordAndCategoryResponse> ExtractKeywordsAndCategoryAsync(
            string companyName,
            string companyUrl,
            int limit = 12,
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

            // Use slugs (stable identifiers) not display names
            var categorySlugs = string.Join(", ", new[]
            {
            "crm-sales","marketing-automation","customer-support","product-analytics","data-platforms",
            "billing-finance","accounting-erp","hr-payroll","collaboration-tools","project-management",
            "devtools","security-iam","itsm","ecommerce-payments","supply-chain","vertical-saas",
            "sales-enablement","knowledge-base","ai-platforms","governance-analytics"
        });

            var user = $@"
You are doing competitive research on {companyName} ({companyUrl}).

IMPORTANT - WHAT COUNTS AS PROVIDED CONTENT
- The provided content is ONLY the public web content you can access by visiting {companyUrl} and obvious product pages linked from it
  (e.g., /product, /features, /pricing, /use-cases, /solutions).
- Do NOT rely on prior knowledge. Use only what you can read from those pages.
- You MUST read at least 2 pages if available (homepage + one product/pricing/features page). If not available, proceed with 1 page and lower confidence.

TASKS
1) Extract a concise list of {limit} high-quality keywords/short phrases representing the company's value/product/positioning (avoid generic utility terms).
2) Choose exactly ONE primary category slug from: [{categorySlugs}]
3) Zero or more TargetSegments from: [Freelancer, SMB, MidMarket, Enterprise, Agencies]
4) Zero or more UserPersonas from: [Marketing, Sales, Product, Support, Analytics/Data, Engineering, Finance, Operations, IT/Admin, Execs]
5) BusinessModel must be [""SaaS""]
6) Provide a short reason (≤ 25 words).
7) Evidence: provide 2–3 exact phrases copied verbatim from the pages you read.
   - COPY/PASTE EXACT PHRASES; DO NOT PARAPHRASE.
8) Confidence: 0–1. If close call, include topAlternatives (slug + confidence).

CONSTRAINTS
- Evidence must be exact phrases copied from the pages you read.
- If you cannot find strong evidence, return fewer evidence items and lower confidence.
- RETURN JSON ONLY.
";

            // Perplexity supports JSON Schema structured outputs via response_format. :contentReference[oaicite:3]{index=3}
            var responseFormat = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "keyword_and_category",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            keywords = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    properties = new
                                    {
                                        keyword = new { type = "string" },
                                        reason = new { type = "string" },
                                        confidence = new { type = "number" } // 0..1
                                    },
                                    required = new[] { "keyword", "reason", "confidence" }
                                }
                            },
                            category = new
                            {
                                type = "object",
                                additionalProperties = false,
                                properties = new
                                {
                                    primary = new { type = "string" }, // slug -> CategoryDto.Primary
                                    targetSegments = new { type = "array", items = new { type = "string" } },
                                    userPersonas = new { type = "array", items = new { type = "string" } },
                                    businessModel = new { type = "array", items = new { type = "string" } },
                                    reason = new { type = "string" },
                                    evidence = new { type = "array", items = new { type = "string" } },
                                    confidence = new { type = "number" },
                                    topAlternatives = new
                                    {
                                        type = "array",
                                        items = new
                                        {
                                            type = "object",
                                            additionalProperties = false,
                                            properties = new
                                            {
                                                primary = new { type = "string" }, // slug -> CategoryAlternative.Primary
                                                confidence = new { type = "number" }
                                            },
                                            required = new[] { "primary", "confidence" }
                                        }
                                    }
                                },
                                required = new[]
                                {
                                    "primary","targetSegments","userPersonas","businessModel",
                                    "reason","evidence","confidence","topAlternatives"
                                }
                            }
                        },
                        required = new[] { "keywords", "category", "self" }
                    }
                }
            };

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var req = new
            {
                model,
                messages = new object[]
                {
                new { role = "user", content = user }
                },
                response_format = responseFormat,
                temperature = 0.1
            };

            // Perplexity Chat Completions endpoint :contentReference[oaicite:4]{index=4}
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

            await _usageLogger.LogAsync(doc.RootElement, "Keywords+Category (Perplexity)", companyId, user);

            try
            {
                return JsonSerializer.Deserialize<KeywordAndCategoryResponse>(
                    clean,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                )!;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to parse Keywords+Category JSON:\n" + (content ?? ""), ex);
            }
        }
    }

}
