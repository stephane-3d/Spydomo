using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public OpenAIService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["OpenAI:ApiKey"]; // Load API key from config

            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new ArgumentNullException("OpenAI API key is missing in configuration.");
            }
        }

        public async Task<string> CleanCompanyNameWithAI(string name)
        {
            string prompt = $"Extract only the company name from: \"{name}\". Remove all extra words and return just the company name.";

            var requestBody = new
            {
                model = "gpt-3.5-turbo", // GPT-3.5 model
                messages = new[]
                {
                new { role = "system", content = "You are an AI that extracts company names from messy text." },
                new { role = "user", content = prompt }
            },
                max_tokens = 20, // Keep it short
                temperature = 0.2 // Low temperature for accuracy
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            HttpResponseMessage response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"OpenAI API Error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var cleanName = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return cleanName?.Trim() ?? name;
        }
    }
}
