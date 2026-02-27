using System.Text.RegularExpressions;

namespace Spydomo.Utilities
{
    public static class UrlHelper
    {
        public static async Task<bool> UrlExistsAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
                return false;

            // Normalize to root URL
            url = uriResult.Scheme + "://" + uriResult.Host;

            try
            {
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var code = (int)response.StatusCode;

                return code >= 200 && code < 400 || code == 403 || code == 429;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Validation error: {ex.Message}");
                return false;
            }
        }

        public static string ExtractDomainFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";

            url = NormalizeUrl(url);

            // ✅ Remove "https://", "http://", "www." and get only the root domain
            string cleanDomain = Regex.Replace(url, @"(https?://|www\.)", "", RegexOptions.IgnoreCase)
                .Split('/')[0]; // ✅ Removes everything after "/"

            return cleanDomain;
        }

        public static string GetHttpsUrl(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return "";

            // If domain already starts with "http://" or "https://", return as is
            if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return domain;
            }

            // Otherwise, add "https://"
            return $"https://{domain}";
        }

        public static string NormalizeUrl(string input)
        {
            if (!input.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                input = "https://" + input;

            try
            {
                var uri = new Uri(input);
                return uri.Host.Replace("www.", "").TrimEnd('/');
            }
            catch
            {
                return input.Trim().ToLower();
            }
        }

        public static string? TryGetRegistrableDomain(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = UrlHelper.GetHttpsUrl(url);

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                return null;

            var host = uri.Host?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(host)) return null;

            // quick normalize
            if (host.StartsWith("www.")) host = host[4..];

            // basic “registrable enough” heuristic (you can swap for a real PSL later)
            // example: app.company.com -> company.com (best-effort)
            var parts = host.Split('.');
            if (parts.Length >= 2)
                return string.Join(".", parts.Skip(parts.Length - 2));

            return host;
        }
    }
}
