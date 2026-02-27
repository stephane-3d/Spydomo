using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// Usage example:
// var httpClient = new HttpClient();
// var brightDataService = new BrightDataService(httpClient);
// var platforms = new List<string> { "g2.com", "capterra.com", "trustradius.com" };
// var results = await brightDataService.SearchPlatformsAsync("Company Name", platforms);

namespace Spydomo.Infrastructure
{
    public class BrightDataService : IBrightDataService
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient _proxyPrimary;
        private readonly HttpClient _proxySerp;
        private readonly string _apiEndpoint;
        private readonly string _apiKey;
        private readonly string _zone;
        private readonly string _proxyZone;
        private readonly List<string> _platforms;
        private readonly Dictionary<string, List<string>> _platformKeywords;
        private readonly ILogger<BrightDataService> _logger;
        private readonly string _scrapingBrowser;
        private readonly string _webhookNotifyUrl;
        private readonly string _scrapingBrowserUrl;
        private readonly string _authCredentials;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public BrightDataService(
            IHttpClientFactory httpClientFactory,
            ILogger<BrightDataService> logger,
            IConfiguration configuration,
            IDbContextFactory<SpydomoContext> dbFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;

            _httpClient = httpClientFactory.CreateClient("BrightDataClient");
            _proxyPrimary = httpClientFactory.CreateClient("BrightDataProxyPrimary");
            _proxySerp = httpClientFactory.CreateClient("BrightDataProxySerp");

            _apiEndpoint = configuration["BrightData:ApiEndpoint"];
            _apiKey = configuration["BrightData:ApiKey"];
            _zone = configuration["BrightData:Zone"];
            _proxyZone = _configuration["BrightData:ProxyZone"];
            _platforms = configuration.GetSection("BrightData:Platforms").Get<List<string>>() ?? new();

            _platformKeywords = new Dictionary<string, List<string>>
            {
                { "g2.com", new List<string> { "reviews", "products" } },
                { "capterra.com", new List<string> { "reviews", "software", "/p/" } },
                { "trustradius.com", new List<string> { "products", "reviews" } },
                { "softwareadvice.com", new List<string> { "profile", "reviews" } },
                { "getapp.com", new List<string> { "reviews", "software", "/a/" } },
                { "gartner.com", new List<string> { "vendor", "product", "market" } }
            };

            _scrapingBrowser = configuration["BrightData:ScrapingBrowser"];
            _webhookNotifyUrl = configuration["BrightData:WebhookNotifyUrl"];

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            _scrapingBrowserUrl = configuration["BrightData:ScrapingBrowserUrl"];
            _authCredentials = configuration["BrightData:ScrapingBrowserCreds"];

            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("Missing configuration: BrightData:ApiKey (env var BrightData__ApiKey).");
        }

        public async Task<List<string>> SearchPlatformsAsync(int companyId, CancellationToken ct = default)
        {
            var searchResults = new List<string>();

            await using var dbContext = await _dbFactory.CreateDbContextAsync(ct);

            var company = await dbContext.Companies.FindAsync(new object?[] { companyId }, ct);
            if (company == null)
            {
                _logger.LogWarning("Company {CompanyId} not found in SearchPlatformsAsync", companyId);
                return searchResults;
            }

            foreach (var platform in _platforms)
            {
                HttpResponseMessage response = null;
                try
                {
                    string query = $"https://www.google.com/search?q={Uri.EscapeDataString(company.Name + " reviews site:" + platform)}&brd_json=1";

                    _logger.LogInformation("Searching {Platform} for {CompanyName}", platform, company.Name);

                    var htmlContent = await FetchGoogleHtmlViaBrightDataApiAsync(query, ct);
                    var resultUrl = ExtractTargetUrl(htmlContent, platform, company);

                    if (resultUrl != null)
                    {
                        searchResults.Add(resultUrl);

                        var platformEntity = await dbContext.DataSourceTypes
                            .FirstOrDefaultAsync(p => p.UrlKeywords.Contains(platform), ct);

                        if (platformEntity != null)
                        {
                            bool alreadyExists = await dbContext.DataSources.AnyAsync(ds =>
                                ds.CompanyId == company.Id &&
                                ds.TypeId == platformEntity.Id &&
                                ds.Url == resultUrl, ct);

                            if (!alreadyExists)
                            {
                                dbContext.DataSources.Add(new DataSource
                                {
                                    CompanyId = company.Id,
                                    TypeId = platformEntity.Id,
                                    Url = resultUrl,
                                    DateCreated = DateTime.UtcNow,
                                    IsActive = true
                                });

                                _logger.LogInformation("✅ Accepted URL: {Url}", resultUrl);
                            }
                            else
                            {
                                _logger.LogInformation("⚠️ Skipped duplicate URL: {Url}", resultUrl);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ DataSourceType not found for platform {Platform}", platform);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("❌ No valid URL found for {CompanyUrl} on {Platform}", company.Url, platform);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing external URLs for {CompanyUrl} on {Platform}", company.Url, platform);
                    if (response != null)
                        _logger.LogWarning("HTTP {Status} from {Host}", (int)response.StatusCode, response.RequestMessage?.RequestUri?.Host);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }

            await dbContext.SaveChangesAsync(ct);
            return searchResults;
        }

        private async Task<string> FetchGoogleHtmlViaBrightDataApiAsync(string url, CancellationToken ct)
        {
            async Task<HttpResponseMessage> SendAsync()
            {
                var payload = new { zone = _proxyZone, url, format = "raw" };

                var req = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint)
                {
                    Content = JsonContent.Create(payload)
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                return await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }

            using var resp = await SendAsync();

            if ((int)resp.StatusCode == 429)
            {
                var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                _logger.LogWarning("BrightData 429. Waiting {Delay} then retrying...", retryAfter);
                await Task.Delay(retryAfter, ct);

                using var resp2 = await SendAsync();
                resp2.EnsureSuccessStatusCode();
                return await resp2.Content.ReadAsStringAsync(ct);
            }

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        public string ExtractTargetUrl(string jsonResponse, string platform, Company company)
        {
            try
            {
                var jsonObject = JObject.Parse(jsonResponse);

                var organicResults = jsonObject["organic"]?.ToObject<List<JObject>>();

                if (organicResults == null || !_platformKeywords.ContainsKey(platform))
                {
                    return null;
                }

                foreach (var result in organicResults)
                {
                    var urlRaw = result["link"]?.ToString();

                    if (string.IsNullOrEmpty(urlRaw))
                    {
                        continue;
                    }

                    var url = urlRaw.ToLowerInvariant();
                    // Ensure URL contains the platform's domain
                    if (!url.Contains(platform.ToLower()))
                    {
                        continue;
                    }

                    // Ensure URL has either the company name or slug variations
                    if (!url.Contains(company.Name.ToLower().Replace(" ", "-")) &&
                        !url.Contains(company.Name.ToLower()) &&
                        !url.Replace("-", "").Contains(company.Name.ToLower()))
                    {
                        continue;
                    }

                    // Ensure the URL contains one of the platform's expected keywords
                    if (_platformKeywords[platform].Any(keyword => url.Contains(keyword)))
                    {
                        return urlRaw; // Return the first matching valid URL
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Google search response: {ex.Message}");
            }

            return null;
        }

        public async Task<string?> FetchHtmlAsync(string targetUrl, CancellationToken ct = default)
            => await FetchHtmlAsync(targetUrl, BrightDataFetchMode.Auto, ct);

        public async Task<string?> FetchHtmlAsync(string targetUrl, BrightDataFetchMode mode, CancellationToken ct = default)
        {
            targetUrl = UrlHelper.GetHttpsUrl(targetUrl);

            if (LooksLikeJsonUrl(targetUrl))
                return await FetchJsonAsync(targetUrl, ct); // never browser for json

            return mode switch
            {
                BrightDataFetchMode.Proxy => await FetchViaProxyAsync(targetUrl, ct),
                BrightDataFetchMode.Unlocker => await FetchViaUnlockerAsync(targetUrl, ct),
                BrightDataFetchMode.Browser => await FetchViaRemoteBrowserAsync(targetUrl, ct),
                _ => await FetchAutoAsync(targetUrl, ct),
            };
        }

        private static bool LooksLikeJsonUrl(string url)
        {
            return url.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || url.Contains(".json?", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> FetchAutoAsync(string url, CancellationToken ct)
        {
            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budgetCts.CancelAfter(TimeSpan.FromSeconds(45)); // whole pipeline budget

            var html = await FetchViaProxyAsync(url, budgetCts.Token);
            if (!string.IsNullOrWhiteSpace(html)) return html;

            html = await FetchViaUnlockerAsync(url, budgetCts.Token);
            if (!string.IsNullOrWhiteSpace(html)) return html;

            return await FetchViaRemoteBrowserAsync(url, budgetCts.Token);
        }


        private static bool IsUsefulHtml(string? body, string? contentTypeHeader = null)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;

            // If we have a content-type, enforce it lightly
            if (!string.IsNullOrWhiteSpace(contentTypeHeader))
            {
                var ct = contentTypeHeader;
                var looksHtml =
                    ct.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("<!doctype", StringComparison.OrdinalIgnoreCase);

                if (!looksHtml) return false;
            }

            // Size gate: weeds out shells / redirect stubs / empty pages
            // (tune by experience; 800 is a good starting point)
            if (body.Length < 800) return false;

            // Generic challenge / blocked / JS-required markers
            // (keep list short & generic)
            var lower = body.AsSpan().ToString().ToLowerInvariant();
            if (lower.Contains("enable javascript") ||
                lower.Contains("verify you are human") ||
                lower.Contains("attention required") ||
                lower.Contains("access denied") ||
                lower.Contains("request blocked") ||
                lower.Contains("captcha") ||
                lower.Contains("cloudflare") ||
                lower.Contains("turnstile"))
                return false;

            return true;
        }

        private static string DecodeBytes(byte[] bytes, string? charset)
        {
            // Most sites are utf-8; charset is often null. If charset is weird, fallback safely.
            try
            {
                if (!string.IsNullOrWhiteSpace(charset))
                {
                    var enc = Encoding.GetEncoding(charset);
                    return enc.GetString(bytes);
                }
            }
            catch { /* ignore */ }

            return Encoding.UTF8.GetString(bytes);
        }

        private async Task<string?> FetchViaRemoteBrowserAsync(string targetUrl, CancellationToken ct)
        {
            try
            {
                await using var remote = await ConnectToRemoteBrowserAsync(ct);

                await using var context = await remote.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = _ua,
                    Locale = "en-US",
                    ViewportSize = new() { Width = 1366, Height = 900 },
                    ServiceWorkers = ServiceWorkerPolicy.Block
                });

                var page = await context.NewPageAsync();
                page.SetDefaultTimeout(15_000);
                page.SetDefaultNavigationTimeout(20_000);

                // Block heavy/noisy resources (keep script/css)
                await page.RouteAsync("**/*", async route =>
                {
                    var t = route.Request.ResourceType;
                    var u = route.Request.Url;

                    if (t is "image" or "font" or "media" or "websocket" or "eventsource"
                        || u.Contains("hotjar") || u.Contains("fullstory") || u.Contains("intercom"))
                        await route.AbortAsync();
                    else
                        await route.ContinueAsync();
                });

                // Navigate (bounded, generic)
                try
                {
                    await page.GotoAsync(targetUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 20_000
                    });
                }
                catch
                {
                    // still attempt to read content if navigation partially succeeded
                }

                // Best-effort settle (bounded)
                await BestEffortSettleAsync(page, maxWaitMs: 8_000, ct);

                // If it’s a SPA, content may exist even if “load” never completes
                string html = "";
                try
                {
                    // Prefer evaluate first: sometimes safer than ContentAsync on flaky remote sessions
                    html = await page.EvaluateAsync<string>("() => document.documentElement?.outerHTML || ''");
                }
                catch { /* ignore */ }

                if (string.IsNullOrWhiteSpace(html) || html.Length < 500)
                {
                    try
                    {
                        html = await page.ContentAsync();
                    }
                    catch (Exception ex) when (ex.GetType().Name == "TargetClosedException")
                    {
                        // The page is gone; return what we have (maybe from evaluate) or null
                        return string.IsNullOrWhiteSpace(html) ? null : html;
                    }
                }

                _logger.LogInformation("Browser final URL: {Url} title={Title}", page.Url, await page.TitleAsync());

                return string.IsNullOrWhiteSpace(html) ? null : html;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote browser fetch failed for {Url}", targetUrl);
                return null;
            }
        }

        private static async Task BestEffortSettleAsync(IPage page, int maxWaitMs, CancellationToken ct)
        {
            const int pollMs = 250;
            const int stablePollsNeeded = 4;
            var stable = 0;
            var lastLen = -1;
            var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                int len = 0;
                try
                {
                    len = await page.EvaluateAsync<int>(@"
() => {
  const c = document.querySelector('main') || document.querySelector('[role=main]') || document.querySelector('#root') || document.body;
  return ((c && c.innerText) ? c.innerText.trim().length : 0);
}");
                }
                catch { /* ignore */ }

                if (len <= lastLen) stable++; else stable = 0;
                lastLen = len;

                if (stable >= stablePollsNeeded) return;

                try { await Task.Delay(pollMs, ct); } catch { return; }
            }
        }


        public Task<RemoteBrowser> ConnectToRemoteBrowserAsync(CancellationToken ct = default)
                => RemoteBrowser.ConnectAsync(_authCredentials, _scrapingBrowserUrl);

        public async Task<string> TriggerScrapingAsync(string datasetId, string url, int pages = 10, string sortFilter = "Most Recent")
        {
            var requestUrl = $"https://api.brightdata.com/datasets/v3/trigger?dataset_id={datasetId}&include_errors=true";

            // it's one or the other, data or notification
            if (!String.IsNullOrEmpty(_webhookNotifyUrl))
                requestUrl += $"&notify={WebUtility.UrlEncode(_webhookNotifyUrl)}";

            var requestBody = $"[{{\"url\":\"{url}\",\"pages\":{pages},\"sort_filter\":\"{sortFilter}\"}}]";

            Console.WriteLine("Trigger URL: " + requestUrl);
            Console.WriteLine("Request Body: " + requestBody);

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Error TriggerScrapingAsync for URL: {url}. Response: {response.StatusCode} - {responseContent}");
                throw new Exception($"Bright Data API error: {response.StatusCode} - {responseContent}");
            }

            return responseContent; // Snapshot ID, but we won’t store it
        }

        public async Task<string> TriggerDiscoveryScrapingAsync(string datasetId, string keyword, string? fromDate = null)
        {
            var requestUrl = $"https://api.brightdata.com/datasets/v3/trigger?dataset_id={datasetId}&include_errors=true";

            if (!string.IsNullOrEmpty(_webhookNotifyUrl))
                requestUrl += $"&notify={WebUtility.UrlEncode(_webhookNotifyUrl)}&type=discover_new&discover_by=keyword";

            var dateFilter = fromDate ?? "All time";

            var payload = new[]
            {
                new Dictionary<string, string>
                {
                    { "keyword", keyword },
                    { "date", dateFilter }
                }
            };

            var json = JsonSerializer.Serialize(payload);

            Console.WriteLine("Trigger URL: " + requestUrl);
            Console.WriteLine("Request Body: " + json);

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Error TriggerDiscoveryScrapingAsync for keyword: {keyword}. Response: {response.StatusCode} - {responseContent}");
                throw new Exception($"Bright Data API error: {response.StatusCode} - {responseContent}");
            }

            return responseContent;
        }

        public async Task<string> TriggerUrlScrapingAsync(
            string datasetId,
            List<Dictionary<string, string>> payloadItems,
            Dictionary<string, string>? extraQueryParams = null)
        {
            var baseUrl = $"https://api.brightdata.com/datasets/v3/trigger?dataset_id={datasetId}&include_errors=true";

            if (!string.IsNullOrEmpty(_webhookNotifyUrl))
                baseUrl += $"&notify={WebUtility.UrlEncode(_webhookNotifyUrl)}";

            if (extraQueryParams != null)
            {
                foreach (var kv in extraQueryParams)
                {
                    baseUrl += $"&{kv.Key}={WebUtility.UrlEncode(kv.Value)}";
                }
            }

            var json = JsonSerializer.Serialize(payloadItems);

            Console.WriteLine("Trigger URL: " + baseUrl);
            Console.WriteLine("Request Body: " + json);

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, baseUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Error TriggerUrlScrapingAsync. Response: {response.StatusCode} - {responseContent}");
                throw new Exception($"Bright Data API error: {response.StatusCode} - {responseContent}");
            }

            return responseContent;
        }

        public async Task<string?> DownloadSnapshotAsync(string snapshotId, CancellationToken ct = default)
        {
            var url = $"https://api.brightdata.com/datasets/v3/snapshot/{snapshotId}?format=json";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("BrightData download failed: snapshotId={SnapshotId} status={StatusCode} body={Body}",
                    snapshotId, (int)response.StatusCode, errorBody);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string?> FetchViaUnlockerAsync(string targetUrl, CancellationToken ct = default)
        {
            try
            {
                var payload = new { zone = _zone, url = targetUrl, format = "raw" };

                using var request = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                // Optional headers (Unlocker usually ignores Accept, but harmless)
                request.Headers.UserAgent.ParseAdd(_ua);
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                request.Content = JsonContent.Create(payload);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20)); // tune

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

                var ctHeader = response.Content.Headers.ContentType?.ToString();
                _logger.LogInformation("Unlocker: {Status} len={Len} ct={CT}",
                    (int)response.StatusCode,
                    bytes.Length,
                    ctHeader ?? "(null)");

                var text = bytes.Length == 0 ? "" : DecodeBytes(bytes, response.Content.Headers.ContentType?.CharSet);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Unlocker error {Status}: {Body}", response.StatusCode, Trunc(text, 800));
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                    return null;

                // If Bright Data returns a JSON wrapper sometimes, unwrap it here.
                if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
                    || LooksLikeJson(text))
                {
                    var unwrapped = TryUnwrapHtmlFromJson(text);
                    if (string.IsNullOrWhiteSpace(unwrapped))
                    {
                        _logger.LogWarning("Unlocker returned JSON but no html field found. Preview={Preview}", Trunc(text, 400));
                        return null;
                    }

                    // ✅ Apply same usefulness criteria to unwrapped HTML
                    if (!IsUsefulHtml(unwrapped, "text/html"))
                    {
                        _logger.LogWarning("Unlocker unwrapped html not useful (len={Len}) for {Url}",
                            unwrapped.Length, targetUrl);
                        return null;
                    }

                    return unwrapped;
                }

                // ✅ Apply same usefulness criteria to raw HTML
                if (!IsUsefulHtml(text, ctHeader))
                {
                    _logger.LogWarning("Unlocker returned non-useful body (len={Len}, ct={CT}) for {Url}",
                        text.Length, ctHeader ?? "(null)", targetUrl);
                    return null;
                }

                return text;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Unlocker fetch timed out for {Url}", targetUrl);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unlocker fetch failed for {Url}", targetUrl);
                return null;
            }
        }

        private const string _ua =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123 Safari/537.36";

        private static bool LooksLikeJson(string s)
        {
            s = s.TrimStart();
            return s.StartsWith("{") || s.StartsWith("[");
        }

        private static string? TryUnwrapHtmlFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Common field names depending on API flavor
                if (root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
                    return body.GetString();

                if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    return content.GetString();

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    if (data.TryGetProperty("body", out var b2) && b2.ValueKind == JsonValueKind.String)
                        return b2.GetString();
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static string Trunc(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";

        public async Task<string?> FetchViaProxyAsync(string targetUrl, CancellationToken ct = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                request.Headers.UserAgent.ParseAdd(_ua);
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                // Per-request timeout so we don't sit ~60s on dead responses
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20)); // tune (dev: 10–20s is nice)

                using var response = await _proxyPrimary.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

                var ctHeader = response.Content.Headers.ContentType?.ToString(); // keep nullable for IsUsefulHtml
                _logger.LogInformation("Proxy: {Status} len={Len} ct={CT} enc=[{Enc}]",
                    (int)response.StatusCode,
                    bytes.Length,
                    ctHeader ?? "(null)",
                    string.Join(",", response.Content.Headers.ContentEncoding));

                var text = bytes.Length == 0 ? "" : DecodeBytes(bytes, response.Content.Headers.ContentType?.CharSet);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Proxy error {Status}: {Body}", response.StatusCode, Trunc(text, 800));
                    return null;
                }

                // ✅ Success but not useful => let Unlocker/Browser try
                if (!IsUsefulHtml(text, ctHeader))
                {
                    _logger.LogWarning("Proxy returned non-useful body (len={Len}, ct={CT}) for {Url}",
                        text.Length, ctHeader ?? "(null)", targetUrl);
                    return null;
                }

                return text;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Proxy fetch timed out for {Url}", targetUrl);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Proxy fetch failed for {Url}", targetUrl);
                return null;
            }
        }
        public async Task<string?> FetchRedditJsonAsync(string targetUrl, CancellationToken ct = default)
        {
            targetUrl = UrlHelper.GetHttpsUrl(targetUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(25));

            // Proxy only. If it fails, skip and move on.
            return await FetchViaProxyTextAsync(targetUrl, expectJson: true, cts.Token);
        }

        public async Task<string?> FetchJsonAsync(string targetUrl, CancellationToken ct = default)
            => await FetchTextAutoAsync(targetUrl, expectJson: true, ct);

        private async Task<string?> FetchTextAutoAsync(string url, bool expectJson, CancellationToken ct)
        {
            url = UrlHelper.GetHttpsUrl(url);

            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budgetCts.CancelAfter(TimeSpan.FromSeconds(25)); // JSON endpoints should be fast

            // Prefer proxy first (cheap)
            var text = await FetchViaProxyTextAsync(url, expectJson, budgetCts.Token);
            if (!string.IsNullOrWhiteSpace(text)) return text;

            // Then unlocker
            text = await FetchViaUnlockerTextAsync(url, expectJson, budgetCts.Token);
            if (!string.IsNullOrWhiteSpace(text)) return text;

            // Never use remote browser for JSON
            return null;
        }

        private async Task<string?> FetchViaProxyTextAsync(string targetUrl, bool expectJson, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                request.Headers.UserAgent.ParseAdd(_ua);
                request.Headers.Accept.ParseAdd(expectJson ? "application/json,*/*;q=0.8" : "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                using var response = await _proxyPrimary.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

                var ctHeader = response.Content.Headers.ContentType?.ToString();
                var text = bytes.Length == 0 ? "" : DecodeBytes(bytes, response.Content.Headers.ContentType?.CharSet);

                _logger.LogInformation("ProxyText: {Status} len={Len} ct={CT}", (int)response.StatusCode, bytes.Length, ctHeader ?? "(null)");

                if (!response.IsSuccessStatusCode) return null;

                // For JSON endpoints: only require non-empty & looks like JSON
                if (expectJson)
                    return LooksLikeJson(text) ? text : null;

                // For HTML: reuse your usefulness gate
                return IsUsefulHtml(text, ctHeader) ? text : null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("ProxyText timed out for {Url}", targetUrl);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProxyText failed for {Url}", targetUrl);
                return null;
            }
        }

        private async Task<string?> FetchViaUnlockerTextAsync(string targetUrl, bool expectJson, CancellationToken ct)
        {
            try
            {
                var payload = new { zone = _zone, url = targetUrl, format = "raw" };

                using var request = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Headers.UserAgent.ParseAdd(_ua);
                request.Headers.Accept.ParseAdd(expectJson
                    ? "application/json,*/*;q=0.8"
                    : "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                request.Content = JsonContent.Create(payload);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(expectJson ? TimeSpan.FromSeconds(45) : TimeSpan.FromSeconds(20));

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                var ctHeader = response.Content.Headers.ContentType?.ToString();
                _logger.LogInformation("UnlockerText: {Status} ct={CT}", (int)response.StatusCode, ctHeader ?? "(null)");

                if (!response.IsSuccessStatusCode) return null;

                // Read body (bounded)
                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                var text = bytes.Length == 0 ? "" : DecodeBytes(bytes, response.Content.Headers.ContentType?.CharSet);

                if (string.IsNullOrWhiteSpace(text)) return null;

                if (expectJson)
                    return LooksLikeJson(text) ? text : null;

                return IsUsefulHtml(text, ctHeader) ? text : null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("UnlockerText timed out for {Url}", targetUrl);
                return null;
            }
            catch (Exception ex)
            {
                // for reddit harvesting, prefer warning
                _logger.LogWarning(ex, "UnlockerText failed for {Url}", targetUrl);
                return null;
            }
        }
    }
}
