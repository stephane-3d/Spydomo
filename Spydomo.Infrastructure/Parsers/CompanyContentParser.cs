using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure.Parsers
{
    public class CompanyContentParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.CompanyContent;

        private readonly IBrightDataService _brightDataService;
        private readonly string _readabilityBaseUrl;
        private readonly IHttpClientFactory _httpFactory;

        public CompanyContentParser(IBrightDataService brightDataService, IConfiguration configuration, IHttpClientFactory httpFactory)
        {
            _brightDataService = brightDataService;
            _readabilityBaseUrl = configuration["ReadabilityService:BaseUrl"];
            _httpFactory = httpFactory;
        }

        public async Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            try
            {
                var html = await _brightDataService.FetchHtmlAsync(url);
                return html ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching content from BrightData for {url}: {ex.Message}");
                return string.Empty;
            }
        }


        public async Task<List<RawContent>> Parse(string html, int companyId, DataSource dataSource, DateTime? lastUpdate, OriginTypeEnum originType = OriginTypeEnum.UserGenerated)
        {
            string rootUrl = ExtractCanonicalRoot(html);

            var articleUrls = ExtractArticleUrls(html, rootUrl);

            var contentList = new List<RawContent>();

            foreach (var articleUrl in articleUrls)
            {
                string readableJson = await FetchReadableContentJson(articleUrl);
                if (string.IsNullOrWhiteSpace(readableJson)) continue;

                var content = JsonSerializer.Deserialize<ReadabilityResult>(readableJson);
                if (content == null || string.IsNullOrWhiteSpace(content.Content)) continue;

                if (lastUpdate.HasValue && content.PostedDate != null && content.PostedDate < lastUpdate.Value)
                    continue;

                contentList.Add(new RawContent
                {
                    CompanyId = companyId,
                    PostedDate = content.PostedDate ?? DateTime.UtcNow,
                    PostUrl = articleUrl,
                    Content = CleanContent(content.Title) + "\r\n\r\n" + CleanContent(content.Content),
                    DataSourceTypeId = (int)dataSource.TypeId,
                    Status = RawContentStatusEnum.NEW,
                    CreatedAt = DateTime.UtcNow,
                    RawResponse = readableJson,
                    OriginType = OriginTypeEnum.CompanyGenerated,
                    EngagementScore = 0
                });
            }

            return contentList;
        }

        private List<string> ExtractArticleUrls(string html, string rootUrl)
        {
            var urls = new HashSet<string>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchors == null) return urls.ToList();

            var rootUri = new Uri(rootUrl);
            var rootHost = rootUri.Host.Replace("www.", "").ToLower();

            foreach (var a in anchors)
            {
                var href = a.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(href)) continue;

                href = href.Split('#')[0]; // remove anchors
                href = href.TrimEnd('/');

                string fullUrl;

                if (href.StartsWith("/"))
                {
                    fullUrl = $"{rootUri.Scheme}://{rootUri.Host}{href}";
                }
                else if (Uri.TryCreate(href, UriKind.Absolute, out var absUri))
                {
                    // Accept only same domain (blog.domain.com or domain.com)
                    if (!absUri.Host.EndsWith(rootHost)) continue;
                    fullUrl = absUri.ToString();
                }
                else
                {
                    // Maybe relative path
                    fullUrl = $"{rootUri.Scheme}://{rootUri.Host}/{href.TrimStart('/')}";
                }

                if (IsBlogLikePath(fullUrl) && !IsIndexOrListingPage(fullUrl))
                {
                    urls.Add(fullUrl);
                }
            }

            return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private bool IsIndexOrListingPage(string url)
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath.ToLowerInvariant();
            var query = uri.Query.ToLowerInvariant();

            // Normalize slashes to prevent trailing issues
            path = path.TrimEnd('/');

            return
                path == "/blog" || path == "/news" || path == "/newsroom" || path == "/press" || path == "/insights" ||
                path == "/articles" || path == "/press-releases" ||
                path.StartsWith("/blog/page/") || path.StartsWith("/news/page/") ||
                path.StartsWith("/blog/tags/") || path.StartsWith("/blog/tag/") ||
                path.Contains("/tag/") || path.Contains("/tags/") || path.Contains("/category/") ||
                query.Contains("page=")
                || path.EndsWith("/news") || path.EndsWith("/newsroom") || path.EndsWith("/press") || path.EndsWith("/press-releases");
        }


        private string CleanContent(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            return raw
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Trim()
                .Replace("  ", " "); // optional: collapse double-spaces
        }


        private bool IsBlogLikePath(string url)
        {
            var blogIndicators = new[] { "/blog", "/news", "/insights", "/press", "/articles", "/press-releases" };

            var uri = new Uri(url.ToLowerInvariant());

            return blogIndicators.Any(ind => uri.Host.StartsWith("blog.") || uri.AbsolutePath.Contains(ind));
        }

        private string ExtractCanonicalRoot(string html)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var canonicalHref = doc.DocumentNode
                    .SelectSingleNode("//link[@rel='canonical']")
                    ?.GetAttributeValue("href", null);

                if (!string.IsNullOrWhiteSpace(canonicalHref) && Uri.IsWellFormedUriString(canonicalHref, UriKind.Absolute))
                {
                    return new Uri(canonicalHref).GetLeftPart(UriPartial.Authority);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to extract canonical URL: {ex.Message}");
            }

            return "https://unknown.com";
        }

        private async Task<string> FetchReadableContentJson(string articleUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(articleUrl)) return string.Empty;
            if (IsProbablyNonHtml(articleUrl)) return string.Empty;

            var http = _httpFactory.CreateClient("Readability");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "extract")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { url = articleUrl }),
                        Encoding.UTF8,
                        "application/json")
                };

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ /extract HTTP {(int)response.StatusCode} for {articleUrl}. Body: {Truncate(body, 400)}");
                    return string.Empty;
                }

                return body;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"⏱️ /extract timeout for {articleUrl}: {ex.Message}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception calling /extract for {articleUrl}: {ex}");
                return string.Empty;
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");

        private static bool IsProbablyNonHtml(string url)
        {
            var path = new Uri(url).AbsolutePath.ToLowerInvariant();
            return path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") ||
                   path.EndsWith(".gif") || path.EndsWith(".webp") || path.EndsWith(".svg") ||
                   path.EndsWith(".pdf") || path.EndsWith(".zip");
        }

    }

}
