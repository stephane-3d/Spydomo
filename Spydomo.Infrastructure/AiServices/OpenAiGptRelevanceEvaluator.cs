using Microsoft.Extensions.Configuration;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using System.Net.Http.Json;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public class OpenAiGptRelevanceEvaluator : IGptRelevanceEvaluator
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public OpenAiGptRelevanceEvaluator(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public async Task<bool> EvaluateRelevanceAsync(int companyId, string companyName, string content, List<string> keywords)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var gptModel = _config["OpenAI:Model"];

            var keywordContext = keywords != null && keywords.Any()
                ? $"The company \"{companyName}\" focuses on the following key themes or differentiators: {string.Join(", ", keywords)}.\n\n"
                : $"The company name is \"{companyName}\".\n\n";

            var prompt = $@"You are a competitive intelligence assistant analyzing online content to decide if it's relevant to a company's positioning.

                {keywordContext}

                Content:
                            ""
                {content}
                ""

                Is this text relevant to the company’s offering, value proposition, audience needs, or competitive position?

                Reply only with 'true' or 'false'."
            ;

            var requestBody = new
            {
                model = gptModel,
                messages = new[] { new { role = "user", content = prompt } },
                max_completion_tokens = 5,
                reasoning_effort = "minimal",
                verbosity = "low"
            };

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception("GPT content relevance check failed: " + error);
            }

            await _usageLogger.LogAsync(json, AiUsagePurposes.RelevanceEvaluator, companyId, prompt);

            var contentResponse = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim().ToLowerInvariant();
            return contentResponse == "true";
        }
    }
}
