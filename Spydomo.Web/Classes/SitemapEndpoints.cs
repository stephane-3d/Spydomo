using Spydomo.Models;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;

namespace Spydomo.Web.Classes
{
    public static class SitemapEndpoints
    {
        public static IEndpointRouteBuilder MapSpydomoSitemaps(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/sitemap.xml", async (
                SpydomoContext db,
                HttpContext http,
                CancellationToken ct) =>
            {
                const string baseUrl = "https://spydomo.com";
                http.Response.ContentType = "application/xml; charset=utf-8";

                // Cache: you can tune this
                http.Response.Headers.CacheControl = "public,max-age=3600"; // 1h
                http.Response.Headers["X-Robots-Tag"] = "all";

                var urls = new List<(string Loc, DateTime? LastMod)>();

                // --- Static pages (keep your list here)
                Add(urls, $"{baseUrl}/");
                Add(urls, $"{baseUrl}/about");
                Add(urls, $"{baseUrl}/faq");
                Add(urls, $"{baseUrl}/privacy");
                Add(urls, $"{baseUrl}/terms");
                Add(urls, $"{baseUrl}/dpa");
                Add(urls, $"{baseUrl}/subprocessors");
                Add(urls, $"{baseUrl}/contact");
                Add(urls, $"{baseUrl}/how-it-works");
                Add(urls, $"{baseUrl}/use-cases");
                Add(urls, $"{baseUrl}/examples");
                Add(urls, $"{baseUrl}/pricing");
                Add(urls, $"{baseUrl}/spydomo-vs-chatgpt");
                Add(urls, $"{baseUrl}/spydomo-vs-ai-agents");
                Add(urls, $"{baseUrl}/spydomo-vs-perplexity");
                Add(urls, $"{baseUrl}/spydomo-vs-competitive-intelligence-software");
                Add(urls, $"{baseUrl}/spydomo-vs-monitoring-alerts");
                Add(urls, $"{baseUrl}/llms.txt");

                // --- Signals Library root
                Add(urls, $"{baseUrl}/signals");

                // --- Public Pulse pages (examples) - only if snapshot exists
                // GroupSnapshotKind.Pulse == 1
                var pulsePages = await (
                    from g in db.CompanyGroups.AsNoTracking()
                    join s in db.GroupSnapshots.AsNoTracking() on g.Id equals s.GroupId
                    where !g.IsPrivate
                       && s.Kind == GroupSnapshotKind.Pulse
                       && s.TimeWindowDays == 30
                    group s by g.Slug into grp
                    select new
                    {
                        Slug = grp.Key,
                        LastUpdatedUtc = grp.Max(x => x.GeneratedAtUtc)
                    }
                ).ToListAsync(ct);

                foreach (var p in pulsePages)
                    Add(urls, $"{baseUrl}/pulse/{p.Slug}", p.LastUpdatedUtc);

                // --- Signals pages (category / signal type / theme)
                // Build only pages that actually exist in data to avoid thin/empty pages.
                var signalCombos = await (
                    from c in db.Companies.AsNoTracking()
                    join cat in db.CompanyCategories.AsNoTracking() on c.PrimaryCategoryId equals cat.Id
                    join si in db.SummarizedInfos.AsNoTracking() on c.Id equals si.CompanyId
                    join sit in db.SummarizedInfoSignalTypes.AsNoTracking() on si.Id equals sit.SummarizedInfoId
                    join st in db.SignalTypes.AsNoTracking() on sit.SignalTypeId equals st.Id
                    join sith in db.SummarizedInfoThemes.AsNoTracking() on si.Id equals sith.SummarizedInfoId
                    join th in db.CanonicalThemes.AsNoTracking() on sith.CanonicalThemeId equals th.Id
                    where st.AllowedInLlm
                       && cat.Slug != null && cat.Slug != ""
                       && st.Slug != null && st.Slug != ""
                       && th.Slug != null && th.Slug != ""
                    select new
                    {
                        CategorySlug = cat.Slug,
                        SignalSlug = st.Slug,
                        ThemeSlug = th.Slug,
                        LastMod = si.Date
                    }
                ).ToListAsync(ct);

                // Categories
                foreach (var g in signalCombos
                             .GroupBy(x => x.CategorySlug)
                             .Select(g => new { Slug = g.Key, LastMod = g.Max(x => x.LastMod) }))
                {
                    Add(urls, $"{baseUrl}/signals/{g.Slug}", g.LastMod);
                }

                // Category + SignalType
                foreach (var g in signalCombos
                             .GroupBy(x => new { x.CategorySlug, x.SignalSlug })
                             .Select(g => new { g.Key.CategorySlug, g.Key.SignalSlug, LastMod = g.Max(x => x.LastMod) }))
                {
                    Add(urls, $"{baseUrl}/signals/{g.CategorySlug}/{g.SignalSlug}", g.LastMod);
                }

                // Category + SignalType + Theme
                foreach (var g in signalCombos
                             .GroupBy(x => new { x.CategorySlug, x.SignalSlug, x.ThemeSlug })
                             .Select(g => new { g.Key.CategorySlug, g.Key.SignalSlug, g.Key.ThemeSlug, LastMod = g.Max(x => x.LastMod) }))
                {
                    Add(urls, $"{baseUrl}/signals/{g.CategorySlug}/{g.SignalSlug}/{g.ThemeSlug}", g.LastMod);
                }

                // Optional: de-dupe just in case
                urls = urls
                    .GroupBy(x => x.Loc)
                    .Select(g => g.OrderByDescending(x => x.LastMod ?? DateTime.MinValue).First())
                    .OrderBy(x => x.Loc, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var xml = BuildSitemapXml(urls);
                await http.Response.WriteAsync(xml, ct);
            });

            endpoints.MapGet("/llms.txt", async (HttpContext http, CancellationToken ct) =>
            {
                const string baseUrl = "https://spydomo.com";
                http.Response.ContentType = "text/plain; charset=utf-8";
                http.Response.Headers.CacheControl = "public,max-age=3600"; // 1h

                // Keep this short and clear.
                // (You can expand later with more structure.)
                var sb = new StringBuilder();
                sb.AppendLine("# Spydomo");
                sb.AppendLine("# Human- and LLM-friendly entry points");
                sb.AppendLine();
                sb.AppendLine($"Home: {baseUrl}/");
                sb.AppendLine($"About: {baseUrl}/about");
                sb.AppendLine($"How it works: {baseUrl}/how-it-works");
                sb.AppendLine($"Use cases: {baseUrl}/use-cases");
                sb.AppendLine($"Examples: {baseUrl}/examples");
                sb.AppendLine($"Market Pulse examples: {baseUrl}/pulse/<slug>");
                sb.AppendLine($"GTM Signals Library: {baseUrl}/signals");
                sb.AppendLine($"Signals category: {baseUrl}/signals/<category>");
                sb.AppendLine($"Signals type: {baseUrl}/signals/<category>/<signal-type>");
                sb.AppendLine($"Signals theme: {baseUrl}/signals/<category>/<signal-type>/<theme>");
                sb.AppendLine();
                sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");

                await http.Response.WriteAsync(sb.ToString(), ct);
            });

            return endpoints;
        }

        private static void Add(List<(string Loc, DateTime? LastMod)> urls, string loc, DateTime? lastMod = null)
            => urls.Add((loc, lastMod));

        private static string BuildSitemapXml(List<(string Loc, DateTime? LastMod)> urls)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">");

            foreach (var u in urls)
            {
                sb.AppendLine("  <url>");
                sb.Append("    <loc>");
                sb.Append(XmlEscape(u.Loc));
                sb.AppendLine("</loc>");

                if (u.LastMod is not null)
                {
                    // Sitemap expects ISO 8601
                    sb.Append("    <lastmod>");
                    sb.Append(u.LastMod.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    sb.AppendLine("</lastmod>");
                }

                sb.AppendLine("  </url>");
            }

            sb.AppendLine("</urlset>");
            return sb.ToString();
        }

        private static string XmlEscape(string value)
            => SecurityElementEscape(value);

        // avoids referencing System.Security directly
        private static string SecurityElementEscape(string str)
            => str
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
    }
}
