using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Spydomo.Infrastructure.ServiceModels;

namespace Spydomo.Infrastructure
{
    public class GoogleSearchService
    {
        private readonly HttpClient _httpProxyClient;
        private readonly ILogger<GoogleSearchService> _logger;
        private readonly IConfiguration _config;

        public GoogleSearchService(IHttpClientFactory httpClientFactory, ILogger<GoogleSearchService> logger, IConfiguration config)
        {
            _httpProxyClient = httpClientFactory.CreateClient("BrightDataProxySerp");
            _logger = logger;
            _config = config;
        }

        public async Task<List<GoogleSearchResult>> SearchAsync(string companyName, string[] extraKeywords, string site = "reddit.com")
        {
            var apiKey = _config["BrightData:ApiKey"];
            var query = BuildQuery(companyName, extraKeywords, site);

            string url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}&as_qdr=w&brd_json=1&num=50";

            var response = await _httpProxyClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Bright Data API call failed: {response.StatusCode}");
                return new List<GoogleSearchResult>();
            }

            var content = await response.Content.ReadAsStringAsync();

            try
            {
                var json = JObject.Parse(content);
                var organic = json["organic"]?.ToObject<List<JObject>>();

                var results = organic?
                    .Select(o => new GoogleSearchResult
                    {
                        Url = o["link"]?.ToString(),
                        Title = o["title"]?.ToString(),
                        Description = o["description"]?.ToString(),
                        DisplayLink = o["display_link"]?.ToString()
                    })
                    .Where(r => !string.IsNullOrEmpty(r.Url))
                    .ToList();

                return results ?? new List<GoogleSearchResult>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Bright Data search results: {ex.Message}");
                return new List<GoogleSearchResult>();
            }
        }

        private string BuildQuery(string companyName, string[] keywords, string site)
        {
            var keywordPart = keywords != null && keywords.Length > 0
                ? $"({string.Join(" OR ", keywords.Select(k => $"\"{k}\""))})"
                : "";

            return $"site:{site} \"{companyName}\" {keywordPart}".Trim();
        }

    }

}
