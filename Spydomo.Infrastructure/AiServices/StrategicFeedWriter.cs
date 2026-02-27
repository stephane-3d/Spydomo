using Microsoft.Extensions.Configuration;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Utilities;
using System.Net.Http.Json;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public class StrategicFeedWriter : IFeedWriter
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public StrategicFeedWriter(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public async Task<string> GenerateFeedTextAsync(StrategicSignal signal)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var gptModel = _config["OpenAI:Model"];

            var prompt = BuildPrompt(signal);

            var requestBody = new
            {
                model = gptModel,
                messages = new[]
                {
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
            var cleaned = JsonHelper.StripJsonCodeBlock(content);

            await _usageLogger.LogAsync(json, "FeedItemGeneration", signal.CompanyId);

            try
            {
                var parsed = JsonDocument.Parse(cleaned);
                return parsed.RootElement.GetProperty("text").GetString() ?? "";
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to parse FeedItem JSON response:\n" + content, ex);
            }
        }

        private string BuildPrompt(StrategicSignal signal)
        {
            var sourceList = string.Join(", ", signal.Sources.Distinct());
            var tagList = string.Join(", ", signal.Tags.Distinct());
            var competitorNote = signal.CompetitorMentions?.Any() == true ? "Includes competitor mention." : "No competitor mentioned.";

            return @$"You are an assistant generating competitive intelligence updates.

                Based on the following strategic signal from a company, generate a short 2–4 sentence feed item that is clear, helpful, and insightful for marketing and product leads.

                Signal Type: Types: [{string.Join(", ", signal.TypeSlug)}];
                Themes: {signal.Themes}
                Tags: {tagList}
                Sources: {sourceList}
                {competitorNote}

                Return only this JSON:
                {{
    
                        ""text"": ""...""
                }}
                ";
        }
    }

}
