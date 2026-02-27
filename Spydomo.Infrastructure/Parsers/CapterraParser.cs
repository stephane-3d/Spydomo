using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spydomo.Infrastructure.Parsers
{
    public class CapterraParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.Capterra;

        private readonly IBrightDataService _brightDataService;
        private readonly IConfiguration _configuration;
        private readonly string CapterraUrl;
        private readonly ILogger<CapterraParser> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public CapterraParser(IConfiguration configuration, IBrightDataService brightDataService, ILogger<CapterraParser> logger, IHttpClientFactory httpClientFactory)
        {
            _brightDataService = brightDataService;
            _configuration = configuration;
            _logger = logger;

            CapterraUrl = _configuration["CapterraScraper:BaseUrl"];
            _httpClientFactory = httpClientFactory;
        }

        public async Task<List<RawContent>> Parse(string htmlResponse, int companyId, DataSource source, DateTime? lastUpdate, OriginTypeEnum originType = OriginTypeEnum.UserGenerated)
        {
            var reviews = new List<RawContent>();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlResponse);

            // Extract reviewIds from script blocks
            var reviewIdMap = ExtractReviewIdsFromScriptTags(htmlDoc);

            // Find all review blocks
            var reviewNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'typo-10') and contains(@class, 'mb-6')]");
            if (reviewNodes == null) return reviews;

            foreach (var reviewNode in reviewNodes)
            {
                var reviewData = new Dictionary<string, object>();

                // Title
                var titleNode = reviewNode.SelectSingleNode(".//h3");
                var rawTitle = titleNode?.InnerText ?? "";
                var title = CleanTitle(rawTitle);

                reviewData["title"] = title;

                // Date
                var dateNode = reviewNode.SelectSingleNode(".//div[contains(@class, 'text-neutral-90') and contains(text(), '20')]");
                string reviewDateStr = dateNode?.InnerText.Trim();
                DateTime? reviewDate = ParseReviewDate(reviewDateStr);
                reviewData["reviewDate"] = reviewDate ?? DateTime.UtcNow;

                // Fix 4: Reviews are sorted newest-first. Once we hit one older than
                // lastUpdate we already have it in the DB — stop processing.
                if (lastUpdate.HasValue && reviewDate.HasValue && reviewDate.Value < lastUpdate.Value)
                {
                    if (_logger?.IsEnabled(LogLevel.Debug) == true)
                        _logger.LogDebug("Stopping parse: review dated {ReviewDate} is older than lastUpdate {LastUpdate}", reviewDate, lastUpdate);
                    break;
                }

                // Rating
                var ratingNode = reviewNode.SelectSingleNode(".//div[@data-testid='Overall Rating-rating']//span[last()]");

                var ratingStr = ratingNode?.InnerText.Trim();
                double.TryParse(ratingStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double ratingValue);

                // Main text
                var mainText = reviewNode.SelectSingleNode(".//div[contains(@class, '!mt-4')]//p");
                reviewData["overall"] = HtmlEntity.DeEntitize(mainText?.InnerText.Trim() ?? "");

                // Pros / Cons / Other labeled fields
                reviewData["pros"] = ExtractLabeledSection(reviewNode, "Pros");
                reviewData["cons"] = ExtractLabeledSection(reviewNode, "Cons");
                reviewData["alternativesConsidered"] = ExtractAlternatives(reviewNode);
                reviewData["reasonsForChoosing"] = ExtractLabeledSection(reviewNode, "Reason for choosing");

                // Try to get reviewId from script tags
                var key = ReviewKey(title, reviewDateStr);
                var reviewId = reviewIdMap.ContainsKey(key) ? reviewIdMap[key] : key;

                string reviewUrl = null;
                if (!string.IsNullOrEmpty(reviewId))
                {
                    reviewUrl = source.Url + (source.Url.EndsWith("/") ? "" : "/") + reviewId;
                }
                else
                {
                    _logger?.LogWarning("Review ID not found for review: Title: {Title} | Date: {Date} | URL: {Url}", title, reviewDateStr, source.Url);
                }

                // Create and store RawContent
                if (!string.IsNullOrEmpty(reviewUrl))
                {
                    var structured = new
                    {
                        Text = reviewData,
                        Metadata = new
                        {
                            Rating = ratingValue
                        }
                    };

                    var contentJson = JsonConvert.SerializeObject(structured, Formatting.None);

                    var feedback = new RawContent
                    {
                        CompanyId = companyId,
                        Content = contentJson,
                        DataSourceTypeId = (int)DataSourceTypeEnum.Capterra,
                        PostUrl = reviewUrl,
                        PostedDate = reviewDate ?? DateTime.UtcNow,
                        Status = RawContentStatusEnum.NEW,
                        CreatedAt = DateTime.UtcNow,
                        RawResponse = reviewNode.InnerText,
                        OriginType = originType
                    };

                    reviews.Add(feedback);
                }
            }
            //throw new Exception("No reviews found in the HTML response.");
            return reviews;
        }

        private static string ExtractLabeledSection(HtmlNode reviewNode, string label)
        {
            var span = reviewNode.SelectSingleNode($".//span[contains(text(), '{label}')]");
            if (span == null) return "";

            // Look for the nearest <p> sibling after the label
            var paragraph = span
                .Ancestors("div").FirstOrDefault()
                ?.SelectSingleNode(".//p");

            return HtmlEntity.DeEntitize(paragraph?.InnerText.Trim() ?? "");
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            return HtmlEntity.DeEntitize(
                raw
                    .Trim()
                    .Trim('\"', '“', '”')  // strip straight and curly quotes
                    .Replace("\u00A0", " ") // non-breaking spaces
                    .Trim()
            );
        }

        private static string ExtractAlternatives(HtmlNode reviewNode)
        {
            var label = reviewNode.SelectSingleNode($".//span[contains(text(), 'Alternatives considered')]");
            if (label == null) return "";

            var parentSpan = label.Ancestors("span").FirstOrDefault();
            if (parentSpan == null) return "";

            var productSpans = parentSpan.SelectNodes(".//a/span");
            if (productSpans == null) return "";

            var names = productSpans
                .Select(s => HtmlEntity.DeEntitize(s.InnerText.Trim()))
                .Where(n => !string.IsNullOrWhiteSpace(n));

            return string.Join(", ", names);
        }


        private Dictionary<string, string> ExtractReviewIdsFromScriptTags(HtmlDocument htmlDoc)
        {
            var reviewIdMap = new Dictionary<string, string>();

            var scriptNodes = htmlDoc.DocumentNode.SelectNodes("//script");
            if (scriptNodes == null) return reviewIdMap;

            foreach (var script in scriptNodes)
            {
                var content = script.InnerText;
                if (!content.Contains("reviewId") && !content.Contains("ReviewId")) continue;

                // Match payloads like self.__next_f.push([1,"..."]);
                var matches = Regex.Matches(content, @"push\(\[\d+,\s*""(?<jsonBlob>.+?)""\s*\]\)");

                foreach (Match match in matches)
                {
                    var encoded = match.Groups["jsonBlob"].Value;

                    try
                    {
                        var decoded = Regex.Unescape(encoded);

                        // Look for exact reviewId/title/writtenOn inside the unescaped string
                        var innerMatches = Regex.Matches(decoded,
                            @"""reviewId"":""(?<id>Capterra___\d+)"",""title"":""(?<title>[^""]+)"",""writtenOn"":""(?<date>[^""]+)""");

                        foreach (Match inner in innerMatches)
                        {
                            var id = inner.Groups["id"].Value;
                            var title = CleanTitle(inner.Groups["title"].Value);
                            var date = inner.Groups["date"].Value.Trim();

                            var key = ReviewKey(title, date);
                            if (!reviewIdMap.ContainsKey(key))
                                reviewIdMap[key] = id;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("⚠️ Skipped a malformed payload: " + ex.Message);
                    }
                }
            }

            Console.WriteLine($"✅ Found {reviewIdMap.Count} reviewId matches.");
            return reviewIdMap;
        }


        public static string ReviewKey(string title, string dateString)
        {
            string Normalize(string input) =>
                (input ?? "").Trim().ToLowerInvariant().Replace("\u00A0", " ")
                .Replace("\"", "")
                .Replace(" ", "_");

            return Normalize(title) + "|" + Normalize(dateString);
        }


        private DateTime? ParseReviewDate(string dateString)
        {
            if (DateTime.TryParseExact(dateString, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate;
            }
            return null;
        }

        public async Task<string> FetchRawHtmlFromNodeAsync(string url)
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsJsonAsync(CapterraUrl, new { url });

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Scraping failed: {response.StatusCode} - {error}");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("html").GetString();
        }

        public async Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            int retries = 1;
            int maxRetries = 2;
            bool stopScraping = false;
            string html = null;

            while (!stopScraping && retries <= maxRetries)
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("NodeScraper");
                    var requestPayload = new { url };
                    var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(CapterraUrl, content);
                    if (response.IsSuccessStatusCode)
                    {
                        html = await response.Content.ReadAsStringAsync();

                        if (string.IsNullOrWhiteSpace(html))
                        {
                            Console.WriteLine("⚠️ Empty HTML content returned, retrying...");
                            retries++;
                            continue;
                        }

                        Console.WriteLine($"✅ Scraping successful on attempt #{retries}.");
                        stopScraping = true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Request failed with status code: {response.StatusCode}");
                        retries++;
                    }
                    await Task.Delay(500 * retries);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Exception during scraping: {ex.Message}");
                    retries++;
                    await Task.Delay(1000);
                }
            }

            if (!stopScraping)
            {
                throw new Exception($"Failed to fetch raw content after {maxRetries} attempts for {url}.");
            }

            return html;
        }

    }
}
