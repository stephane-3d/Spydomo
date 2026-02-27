using Microsoft.Extensions.Configuration;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Utilities;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public class OpenAiFeedItemExtractor : IFeedItemExtractor
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public OpenAiFeedItemExtractor(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public async Task<(string Gist, string WhyItMatters)> GenerateFeedItemAsync(
            string summaryGist,
            List<string> themes,
            string companyName,
            string contextSummary,
            int? companyId = null)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var gptModel = _config["OpenAI:Model"];

            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine($@"You are a competitive intelligence assistant for a digital marketer.

                Your job is to help him uncover valuable insights about the competitive landscape.");

            if (!string.IsNullOrWhiteSpace(contextSummary))
            {
                promptBuilder.AppendLine($@"Especially, you should analyze through the lens of this context:
                    {contextSummary}");
            }

            promptBuilder.AppendLine($@"Help them understand what matters in the following market activity about {companyName}:

                Summary:
                ""{summaryGist}"" 

                Themes:
                {string.Join(", ", themes)}

                Please return:
                - A short headline-style summary of this finding (1 sentence max)
                - A brief reason why this matters to a digital marketer (1 sentence)

                Return JSON:
                {{
                    ""gist"": ""..."",
                    ""why"": ""...""
                }}");

            var prompt = promptBuilder.ToString();


            var requestBody = new
            {
                model = gptModel,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_completion_tokens = 200,
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
            var cleanJson = JsonHelper.StripJsonCodeBlock(content);

            await _usageLogger.LogAsync(json, "FeedItemExtraction", companyId);

            try
            {
                var parsed = JsonDocument.Parse(cleanJson);
                return (
                    parsed.RootElement.GetProperty("gist").GetString() ?? "",
                    parsed.RootElement.GetProperty("why").GetString() ?? ""
                );
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to parse FeedItem JSON response:\n" + content, ex);
            }
        }
    }
}
