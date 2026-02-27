using Microsoft.Extensions.Configuration;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Utilities;
using System.Net.Http.Json;
using System.Text.Json;


namespace Spydomo.Infrastructure.AiServices
{
    public class OpenAiKeywordExtractor : IKeywordExtractor
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public OpenAiKeywordExtractor(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public Task<KeywordAndCategoryResponse> ExtractKeywordsAndCategoryAsync(
           string companyName,
           string companyUrl,
           int limit = 12,
           int? companyId = null,
           CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        /*public async Task<KeywordAndCategoryResponse> ExtractKeywordsAndCategoryAsync(
                string visibleText, int limit = 12, int? companyId = null, CancellationToken ct = default)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var gptModel = _config["OpenAI:Model"] ?? "gpt-5-mini"; // <- recommended default

            var system = "You are a precise market research assistant. Work only with the provided content.";

            var user = $@"
                TASKS
                1) Extract a concise list of {limit} high-quality keywords/short phrases that represent the company's value, product, and positioning (avoid utility terms).
                2) Choose exactly ONE PrimaryCategory from:
                [CRM & Sales,
Marketing Automation,
Customer Support,
Product Analytics,
Data Platforms,
Billing & Finance,
Accounting & ERP,
HR & Payroll,
Collaboration Tools,
Project Management,
DevTools,
Security & IAM,
ITSM,
E-commerce & Payments,
Supply Chain,
Vertical SaaS,
Sales Enablement,
Knowledge Base,
AI Platforms,
Governance & Analytics]
                3) If (and only if) PrimaryCategory = SEO, choose exactly ONE SeoSubCategory from:
                [KeywordResearch, RankTracking, LinkIntelligence, TechnicalSEO, ContentOptimization, SerpCompetitor, LocalSEO]
                4) Zero or more TargetSegments from: [Freelancer, SMB, MidMarket, Enterprise, Agencies]
                5) Zero or more UserPersonas from: [Marketing, Support, Product, Sales, DataAnalytics, Engineering, Finance, Execs]
                6) Set BusinessModel to [""SaaS""].
                7) Provide a short reason for the category and quote 2–4 exact phrases as evidence.
                8) Confidence: 0–1. If close call, include topAlternatives.

                CONSTRAINTS
                - Only use categories listed above.
                - Base your decision ONLY on the provided content.
                - Reasons ≤ 25 words. Evidence must be exact substrings.

                RETURN JSON ONLY:

                {{
                  ""keywords"": [
                    {{ ""keyword"": ""..."", ""reason"": ""..."", ""confidence"": 1 }}
                  ],
                  ""category"": {{
                    ""primary"": ""MarketingAnalyticsReporting"",
                    ""seoSubCategory"": null,
                    ""targetSegments"": [""Agencies""],
                    ""userPersonas"": [""Marketing"",""DataAnalytics""],
                    ""businessModel"": [""SaaS""],
                    ""reason"": ""..."",
                    ""evidence"": [""..."",""...""],
                    ""confidence"": 0.0,
                    ""topAlternatives"": [ {{ ""primary"": ""SEO"", ""confidence"": 0.31 }} ]
                  }}
                }}

                CONTENT
                ---
                {visibleText}";

            // Optional: structured outputs for stricter JSON (supported on latest models).
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
                                        confidence = new { type = "integer" }
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
                                    primary = new { type = "string" },
                                    seoSubCategory = new
                                    {
                                        anyOf = new object[] {
                                          new { type = "string" },
                                          new { type = "null" }
                                        }
                                    },
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
                                                primary = new { type = "string" },
                                                confidence = new { type = "number" }
                                            },
                                            required = new[] { "primary", "confidence" }
                                        }
                                    }
                                },
                                required = new[] { "primary", "seoSubCategory", "targetSegments", "userPersonas", "businessModel", "reason", "evidence", "confidence", "topAlternatives" }
                            }
                        },
                        required = new[] { "keywords", "category" }
                    }
                }
            };

            var req = new
            {
                model = gptModel,
                messages = new object[] {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                },
                // If your model/version rejects structured outputs, comment out the next line and rely on plain JSON.
                response_format = responseFormat,
                max_completion_tokens = 800,
                reasoning_effort = "minimal",
                verbosity = "low"
            };

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var httpRes = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", req);

            var raw = await httpRes.Content.ReadAsStringAsync();
            if (!httpRes.IsSuccessStatusCode)
                throw new Exception($"OpenAI API call failed: {raw}");

            using var doc = JsonDocument.Parse(raw);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            var clean = JsonHelper.StripJsonCodeBlock(content);

            // log usage/cost if you have a logger
            await _usageLogger.LogAsync(doc, "Keywords+Category", companyId, system + "\n" + user);

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<KeywordAndCategoryResponse>(clean, options)!;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to parse Keywords+Category JSON from GPT:\n" + content, ex);
            }
        }

        */
    }

}
