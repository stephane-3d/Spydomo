using Microsoft.Extensions.Configuration;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spydomo.Infrastructure
{
    public class PageContentFetcherService
    {
        private readonly IBrightDataService _brightDataService;
        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;

        public PageContentFetcherService(IBrightDataService brightDataService, HttpClient httpClient, IConfiguration configuration)
        {
            _brightDataService = brightDataService;
            _httpClient = httpClient;
            _baseUrl = configuration["ReadabilityService:BaseUrl"]?.TrimEnd('/');
        }

        public async Task<RedditThreadResult> FetchRedditPostAndCommentsAsync(string redditUrl)
        {
            try
            {
                var jsonUrl = redditUrl.TrimEnd('/') + ".json";

                // If needed, route through Bright Data
                var jsonContent = await _brightDataService.FetchHtmlAsync(jsonUrl);

                if (string.IsNullOrWhiteSpace(jsonContent))
                    return null;

                var doc = JsonDocument.Parse(jsonContent);

                var postData = doc.RootElement[0]
                    .GetProperty("data")
                    .GetProperty("children")[0]
                    .GetProperty("data");

                string title = postData.GetProperty("title").GetString();
                string body = postData.GetProperty("selftext").GetString();
                double createdUtcRaw = postData.GetProperty("created_utc").GetDouble();
                DateTime createdUtc = DateTimeOffset.FromUnixTimeSeconds((long)createdUtcRaw).UtcDateTime;

                var comments = doc.RootElement[1]
                    .GetProperty("data")
                    .GetProperty("children")
                    .EnumerateArray()
                    .Where(c => c.GetProperty("kind").GetString() == "t1") // t1 = comment
                    .Select(c => c.GetProperty("data").GetProperty("body").GetString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Take(5)
                    .ToList();

                return new RedditThreadResult
                {
                    Title = title,
                    Body = body,
                    TopComments = comments,
                    CreatedUtc = createdUtc
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error fetching Reddit JSON: " + ex.Message);
                return null;
            }
        }


        public async Task<ReadabilityResult> FetchMainContentWithReadabilityAsync(string url)
        {
            try
            {
                var rawHtml = await _brightDataService.FetchHtmlAsync(url);

                if (string.IsNullOrWhiteSpace(rawHtml))
                    return null;

                var payload = new { html = rawHtml, url };
                var json = JsonSerializer.Serialize(payload);

                var response = await _httpClient.PostAsync(
                    $"{_baseUrl}/extract-html",
                    new StringContent(json, Encoding.UTF8, "application/json")
                );

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Readability failed: " + response.StatusCode);
                    return null;
                }

                var resultString = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ReadabilityResult>(resultString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Extract relative date from LinkedIn HTML
                result.PostedDate = ExtractRelativeDateFromLinkedInHtml(result.Content);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error calling readability service: " + ex.Message);
                return null;
            }
        }

        public static DateTime? ExtractRelativeDateFromLinkedInHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            // Example matches: "1mo", "2y", "3w", "5d", "8h"
            var match = Regex.Match(html, @"(\d+)\s*(mo|w|d|h|y)\b", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            int value = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value.ToLower();

            return unit switch
            {
                "h" => DateTime.UtcNow.AddHours(-value),
                "d" => DateTime.UtcNow.AddDays(-value),
                "w" => DateTime.UtcNow.AddDays(-7 * value),
                "mo" => DateTime.UtcNow.AddMonths(-value),
                "y" => DateTime.UtcNow.AddYears(-value),
                _ => null
            };
        }

        public async Task<RenderHtmlResponse?> FetchRenderedHtmlAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                throw new InvalidOperationException("ReadabilityService:BaseUrl is not configured.");

            try
            {
                var payload = new { url };
                var json = JsonSerializer.Serialize(payload);

                var response = await _httpClient.PostAsync(
                    $"{_baseUrl}/render-html",
                    new StringContent(json, Encoding.UTF8, "application/json"),
                    ct
                );

                var body = await response.Content.ReadAsStringAsync(ct);

                // bubble up errors from node (502/500 etc) by still parsing if possible
                var parsed = JsonSerializer.Deserialize<RenderHtmlResponse>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                return parsed;
            }
            catch
            {
                return null;
            }
        }

    }
}
