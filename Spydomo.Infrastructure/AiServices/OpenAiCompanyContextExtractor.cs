using Microsoft.Extensions.Configuration;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using System.Net.Http.Json;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public class OpenAiCompanyContextExtractor : ICompanyContextExtractor
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public OpenAiCompanyContextExtractor(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public async Task<CompanyContextResult> ExtractContextAsync(string visibleText, int? companyId = null)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var gptModel = _config["OpenAI:Model"];

            var prompt = @$"You are a market analyst assistant. Based on the following company text, provide a short summary (2–3 sentences) that describes:
                - What the company does
                - Who they serve
                - What differentiates them
                - What core problem they solve

                Return your response as plain text. Do not include headings or labels.

                Content:
                ---
                {visibleText}
            ";

            var requestBody = new
            {
                model = gptModel,
                messages = new[] {
                    new { role = "user", content = prompt }
                },
                max_completion_tokens = 300,
                reasoning_effort = "minimal",
                verbosity = "low"
            };

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API call failed: {error}");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            await _usageLogger.LogAsync(json, "Company Context Extraction", companyId);

            return new CompanyContextResult
            {
                Context = content?.Trim()
            };
        }
    }
}
