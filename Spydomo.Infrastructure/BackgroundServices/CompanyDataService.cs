using Hangfire;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.AiServices;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class CompanyDataService
    {
        private readonly ILogger<CompanyDataService> _logger;
        private Dictionary<int, DataSourceType> _socialPlatforms;
        private Task<Dictionary<int, DataSourceType>>? _loadTask;
        private readonly ISlugService _slugService;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IBrightDataService _brightDataService;
        private readonly IKeywordExtractor _gptExtractor;
        private readonly PageContentFetcherService _pageContentFetchService;

        public sealed record SelfMeta(string? Title, string? Description, string? PositioningH1);
        public sealed record BestContentResult(string VisibleText, SelfMeta Self);

        private static readonly char[] PunctuationToTrim = new[] {
            ',', '.', '!', '?', ':', ';', '"', '\'', '(', ')', '[', ']', '{', '}', '—', '-', '*', '•', '/'
        };

        private static readonly HashSet<string> StopWords = new HashSet<string>
        {
            // Generic English stopwords
            "about", "this", "that", "with", "from", "your", "more", "they", "what", "have",
            "just", "like", "some", "when", "then", "were", "them", "into", "also", "than",
            "very", "make", "made", "each", "many", "been", "used", "over", "most", "other",

            // Marketing filler
            "free", "trial", "start", "get", "now", "today", "see", "features", "welcome","shared",
            "best","simple","demo","create","read","custom",

            // Latin / Lorem Ipsum filler
            "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing",
            "elit", "tellus", "neque", "mauris", "nibh",

            // Punctuation/noise
            "&amp", "nbsp", "ndash", "quot", "&"
        };

        public Task ProcessJobHangfireAsync(int companyId)
            => ProcessJobAsync(companyId, CancellationToken.None);

        public CompanyDataService(
          ILogger<CompanyDataService> logger,
          ISlugService slugService,
          IConfiguration configuration,
          IDbContextFactory<SpydomoContext> dbFactory,
          IBrightDataService brightDataService,
          IKeywordExtractor gptExtractor,
          PageContentFetcherService pageContentFetchService)
        {
            _logger = logger;
            _socialPlatforms = new Dictionary<int, DataSourceType>();
            _slugService = slugService;
            _dbFactory = dbFactory;
            _brightDataService = brightDataService;
            _gptExtractor = gptExtractor;
            _pageContentFetchService = pageContentFetchService;
        }

        public async Task InitializeAsync()
        {
            _socialPlatforms = await GetSocialPlatformsAsync();
        }

        [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
        public async Task ExtractDataAsync()
        {
            _logger.LogInformation("⏱ CompanyDataService > ExtractDataAsync started");

            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            // Pick a small batch
            var companies = await dbContext.Companies
                .Where(c => c.Status == "NEW")
                .OrderBy(c => c.Id)
                .Take(20)
                .ToListAsync();

            if (companies.Count == 0)
            {
                _logger.LogInformation("No NEW companies to process.");
                return;
            }

            // Mark as PROCESSING up-front (so they won't be picked again if anything happens)
            foreach (var c in companies)
                c.Status = "PROCESSING";

            await dbContext.SaveChangesAsync();

            foreach (var company in companies)
            {
                try
                {
                    _logger.LogInformation("Processing {Name} (Id={Id})...", company.Name, company.Id);

                    await ProcessJobAsync(company.Id);

                    company.Status = "PROCESSED"; // or "COMPLETED" if that's your canonical value
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Company data failed for {Url} (Id={Id})", company.Url, company.Id);
                    company.Status = "FAILED";
                }
                finally
                {
                    // Persist per-company so progress isn't lost if a later one fails/crashes
                    await dbContext.SaveChangesAsync();
                }
            }

            _logger.LogInformation("✅ CompanyDataService > ExtractDataAsync done");
        }

        public async Task ProcessJobAsync(int companyId, CancellationToken ct = default)
        {
            const int maxRetries = 3;
            const int delayBetweenRetriesMs = 5000;

            await using var dbContext = await _dbFactory.CreateDbContextAsync(ct);
            var company = await dbContext.Companies.FindAsync(companyId, ct);

            _logger.LogInformation("Processing Company {Id}.", companyId);

            if (company == null)
            {
                _logger.LogWarning("Company {Id} not found when processing job.", companyId);
                return;
            }

            var validUrl = UrlHelper.GetHttpsUrl(company.Url);

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    company.Status = "PROCESSING";
                    company.RetryCount = attempt;
                    await dbContext.SaveChangesAsync();

                    // --- Fetch & parse HTML ---

                    var htmlContent = await _brightDataService.FetchHtmlAsync(validUrl, BrightDataFetchMode.Auto, ct);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(htmlContent);

                    var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                    var rawName = titleNode?.InnerText.Trim() ?? "Unknown Company";

                    company.Name = await GetCompanyNameFromTitle(rawName, validUrl);
                    company.Slug = await _slugService.GenerateUniqueSlugAsync(
                        company.Name, EntityType.Company);

                    await using var remote = await _brightDataService.ConnectToRemoteBrowserAsync();
                    await SaveCompanyKeywordsAndCategoryAsync(
                        company.Id,
                        validUrl,
                        doc,
                        remote,
                        topKeywordLimit: 12);

                    var socialLinks = await ExtractSocialAndBlogLinksWithRenderedFallbackAsync(validUrl, ct);
                    if (socialLinks.Any())
                    {
                        _logger.LogInformation("Processing {Count} social links.", socialLinks.Count);
                        await SaveSocialLinksAsync(companyId, socialLinks);
                    }
                    else
                        _logger.LogInformation("No social links found");

                    company.LastCompanyDataUpdate = DateTime.UtcNow;
                    company.Status = "COMPLETED";
                    await dbContext.SaveChangesAsync();
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Attempt {Attempt} failed for {Url}", attempt, company.Url);

                    if (attempt == maxRetries)
                    {
                        company.Status = "FAILED";
                        company.RetryCount = attempt;
                        await dbContext.SaveChangesAsync();
                    }
                    else
                    {
                        company.RetryCount = attempt;
                        await dbContext.SaveChangesAsync();
                        await Task.Delay(delayBetweenRetriesMs, ct);
                    }
                }
            }

            // --- BrightData platform links ---

            try
            {
                var brightDataResults = await _brightDataService.SearchPlatformsAsync(companyId);

                if (brightDataResults.Any())
                {
                    _logger.LogInformation(
                        "BrightData found {Count} links for {Name}.",
                        brightDataResults.Count,
                        company.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error when processing brightdata for {Url}", company.Url);
            }
        }

        private async Task SaveCompanyKeywordsAndCategoryAsync(
            int companyId,
            string companyUrl,
            HtmlDocument staticDoc,
            RemoteBrowser remote,
            int topKeywordLimit = 12,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(companyUrl))
                throw new ArgumentException("companyUrl is required.", nameof(companyUrl));

            companyUrl = UrlHelper.GetHttpsUrl(companyUrl);

            var best = await GetBestContentAsync(companyUrl, staticDoc, remote, minUsefulChars: 1200, ct);
            var self = best.Self;
            
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // 1) One-shot LLM extraction OUTSIDE transaction/retry unit
            // (So SQL transient retries won't call the LLM multiple times)
            var companyName = await db.Companies
                .Where(c => c.Id == companyId)
                .Select(c => c.Name)
                .FirstAsync(ct);

            var res = await _gptExtractor.ExtractKeywordsAndCategoryAsync(companyName!, companyUrl, topKeywordLimit, companyId, ct);

            // 2) Use retrying execution strategy for the transactional DB work
            
            var strategy = db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                // Replace existing keywords
                await db.CompanyKeywords
                    .Where(k => k.CompanyId == companyId /* && !k.IsPinned */)
                    .ExecuteDeleteAsync(ct);

                var toInsert = res.Keywords
                    .Where(k => !string.IsNullOrWhiteSpace(k.Keyword))
                    .DistinctBy(k => k.Keyword, StringComparer.OrdinalIgnoreCase)
                    .Select(k => new CompanyKeyword
                    {
                        CompanyId = companyId,
                        Keyword = k.Keyword.Trim(),
                        Reason = k.Reason?.Trim(),
                        Confidence = Math.Clamp(k.Confidence, 0, 1)
                    })
                    .ToList();

                if (toInsert.Count > 0)
                    await db.CompanyKeywords.AddRangeAsync(toInsert, ct);

                // Update company category fields
                var company = await db.Companies.FirstAsync(c => c.Id == companyId, ct);

                var slug = res.Category.Primary?.Trim();

                int? categoryId = null;

                if (!string.IsNullOrWhiteSpace(slug))
                {
                    categoryId = await db.CompanyCategories
                        .Where(x => x.Slug == slug)
                        .Select(x => (int?)x.Id)
                        .FirstOrDefaultAsync(ct);
                }

                // If unknown slug, keep null and log / store legacy if you want.
                company.PrimaryCategoryId = categoryId;
                company.CategoryReason = res.Category.Reason;
                company.CategoryConfidence = (decimal)Math.Round(res.Category.Confidence, 3);
                company.CategoryEvidenceJson = JsonSerializer.Serialize(res.Category.Evidence ?? new List<string>());

                company.SelfTitle = self.Title;
                company.SelfDescription = self.Description;
                company.SelfPositioning = self.PositioningH1;

                // Target Segments (ID-based sync)
                var wantedSegNames = (res.Category.TargetSegments ?? new List<string>()).ToList();
                var desiredSegIds = await db.TargetSegments
                    .Where(t => wantedSegNames.Contains(t.Name))
                    .Select(t => t.Id)
                    .ToListAsync(ct);

                var desiredSegIdSet = desiredSegIds.ToHashSet();

                var currentSegs = await db.CompanyTargetSegments
                    .Where(x => x.CompanyId == companyId)
                    .ToListAsync(ct);

                var existingSegIdSet = currentSegs.Select(x => x.TargetSegmentId).ToHashSet();

                db.CompanyTargetSegments.RemoveRange(
                    currentSegs.Where(x => !desiredSegIdSet.Contains(x.TargetSegmentId))
                );

                foreach (var id in desiredSegIdSet.Except(existingSegIdSet))
                {
                    db.CompanyTargetSegments.Add(new CompanyTargetSegment
                    {
                        CompanyId = companyId,
                        TargetSegmentId = id
                    });
                }

                // User Personas (ID-based sync)
                var wantedPersonaNames = (res.Category.UserPersonas ?? new List<string>()).ToList();
                var desiredPersonaIds = await db.UserPersonas
                    .Where(p => wantedPersonaNames.Contains(p.Name))
                    .Select(p => p.Id)
                    .ToListAsync(ct);

                var desiredPersonaIdSet = desiredPersonaIds.ToHashSet();

                var currentPers = await db.CompanyUserPersonas
                    .Where(x => x.CompanyId == companyId)
                    .ToListAsync(ct);

                var existingPersonaIdSet = currentPers.Select(x => x.UserPersonaId).ToHashSet();

                db.CompanyUserPersonas.RemoveRange(
                    currentPers.Where(x => !desiredPersonaIdSet.Contains(x.UserPersonaId))
                );

                foreach (var id in desiredPersonaIdSet.Except(existingPersonaIdSet))
                {
                    db.CompanyUserPersonas.Add(new CompanyUserPersona
                    {
                        CompanyId = companyId,
                        UserPersonaId = id
                    });
                }

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }


        private async Task<BestContentResult> GetBestContentAsync(
            string url,
            HtmlDocument? staticDoc,
            RemoteBrowser remote,
            int minUsefulChars = 1200,
            CancellationToken ct = default)
        {
            url = UrlHelper.GetHttpsUrl(url);

            // --- Static first (cheap)
            var staticText = ExtractStaticVisibleText(staticDoc);
            var staticMeta = ExtractSelfMetaFromStatic(staticDoc);

            // If static is already good enough AND we have the three self fields, bail early
            if (staticText.Length >= minUsefulChars &&
                staticMeta.Title is not null &&
                staticMeta.Description is not null &&
                staticMeta.PositioningH1 is not null)
            {
                return new BestContentResult(NormalizeText(staticText), staticMeta);
            }

            // --- Single Playwright context for all rendered work
            await using var context = await remote.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123 Safari/537.36",
                Locale = "en-US",
                ViewportSize = new() { Width = 1366, Height = 900 },
                ServiceWorkers = ServiceWorkerPolicy.Block
            });

            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(15000);
            page.SetDefaultNavigationTimeout(20000);

            // Block heavy/noisy resources
            await page.RouteAsync("**/*", async route =>
            {
                var t = route.Request.ResourceType;
                var u = route.Request.Url;
                if (t is "image" or "font" or "media" 
                    || u.Contains("hotjar") || u.Contains("fullstory") || u.Contains("intercom"))
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            });

            // Helper: navigate & extract both visible text and meta in a single eval
            static async Task<(string visible, SelfMeta meta)> ExtractFromAsync(
    IPage page,
    string targetUrl,
    bool captureMeta,
    int timeoutMs,
    CancellationToken ct)
            {
                // Always return *something*; never throw.
                try
                {
                    // Navigate (bounded)
                    try
                    {
                        await page.GotoAsync(
                            targetUrl,
                            new PageGotoOptions
                            {
                                Timeout = timeoutMs,
                                WaitUntil = WaitUntilState.DOMContentLoaded
                            });

                        // Body should exist for almost all pages
                        await page.WaitForSelectorAsync("body", new() { Timeout = Math.Min(5000, timeoutMs) });
                    }
                    catch
                    {
                        // best effort: proceed to evaluation anyway
                    }

                    // Best-effort SPA settle: wait until text grows OR stabilizes, but bounded.
                    // This avoids fragile selectors (#__next) and avoids NetworkIdle traps.
                    await BestEffortSettleAsync(page, maxWaitMs: 8000, ct);

                    // Run extraction script (bounded by page default timeout + ct)
                    const string script = @"
(args) => {
  const reject = new Set(['SCRIPT','STYLE','NOSCRIPT','IFRAME','SVG','CANVAS','TEMPLATE']);
  const isHidden = (el) => {
    const cs = window.getComputedStyle(el);
    return cs.display === 'none' || cs.visibility === 'hidden' || el.getAttribute('aria-hidden') === 'true';
  };

  const container =
    document.querySelector('main') ||
    document.querySelector('[role=main]') ||
    document.querySelector('#root') ||
    document.body;

  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT, {
    acceptNode: n => {
      const p = n.parentElement;
      if (!p || reject.has(p.tagName) || isHidden(p)) return NodeFilter.FILTER_REJECT;
      const t = (n.nodeValue || '').replace(/\s+/g,' ').trim();
      return t ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
    }
  });

  const out = [];
  while (walker.nextNode()) out.push((walker.currentNode.nodeValue || '').replace(/\s+/g,' ').trim());
  const dedup = Array.from(new Set(out.filter(s => s.length >= 3)));
  const visible = dedup.join('\n');

  const decodeEntities = (s) => {
    if (!s) return s;
    const d = document.createElement('textarea');
    d.innerHTML = s;
    return d.value;
  };

  const parseVariants = (node) => {
    let variants = [];
    let raw =
      node.getAttribute('data-strings') ??
      node.getAttribute('data-words') ??
      node.getAttribute('data-rotate') ??
      node.getAttribute('data-type');

    if (raw) {
      let txt = decodeEntities(raw).trim();
      try {
        const parsed = JSON.parse(txt);
        if (Array.isArray(parsed)) variants = parsed.map(s => String(s).trim()).filter(Boolean);
      } catch {
        const qs = Array.from(txt.matchAll(/""([^""]+)""|'([^']+)'/g))
          .map(m => (m[1] ?? m[2]))
          .filter(Boolean);

        if (qs.length > 0) {
          variants = qs.map(s => s.trim()).filter(Boolean);
        } else {
          txt = txt.replace(/^[\[\(\{]+|[\]\)\}]+$/g, '');
          variants = txt.split(/[|,;\/]+/).map(s => s.trim()).filter(Boolean);
        }
      }
    }

    if (variants.length === 0) {
      variants = Array.from(node.querySelectorAll('*'))
        .map(n => (n.textContent || '').trim())
        .filter(Boolean);
    }

    variants = variants
      .map(v => v.replace(/^['""“”]+|['""“”]+$/g, '').trim())
      .filter(Boolean)
      .filter((v, i, a) => a.indexOf(v) === i);

    return variants;
  };

  const normalizeTypewriter = (root) => {
    if (!root) return;
    const sel = [
      '[data-strings]','[data-words]','[data-rotate]','[data-type]',
      '.typewriter','[class*=""typewriter""]','[class*=""typed""]','[class*=""typewrite""]'
    ].join(',');
    root.querySelectorAll(sel).forEach(node => {
      const variants = parseVariants(node);
      if (variants.length > 1) node.textContent = variants.join(' / ');
    });
  };

  let title = null, desc = null, h1 = null;

  if (args && args.captureMeta) {
    const meta = (n) =>
      document.querySelector(`meta[name='${n}']`) || document.querySelector(`meta[property='${n}']`);

    title = (document.title || '').trim() || null;
    desc  = (meta('description')?.getAttribute('content')
          || meta('og:description')?.getAttribute('content')
          || '').trim() || null;

    const h1El = document.querySelector('h1');
    if (h1El) {
      const clone = h1El.cloneNode(true);
      normalizeTypewriter(clone);
      h1 = (clone.innerText || '').replace(/\s+/g,' ').trim() || null;
    }
  }

  return JSON.stringify({ visible, title, desc, h1 });
}";

                    string json;
                    try
                    {
                        json = await page.EvaluateAsync<string>(script, new { captureMeta });
                    }
                    catch
                    {
                        // If evaluate fails (execution context destroyed, CSP edge cases, etc.), return empty.
                        return ("", new SelfMeta(null, null, null));
                    }

                    System.Text.Json.Nodes.JsonObject? obj;
                    try
                    {
                        obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json!);
                    }
                    catch
                    {
                        return ("", new SelfMeta(null, null, null));
                    }

                    var visible = (string?)obj?["visible"] ?? "";
                    var title = (string?)obj?["title"];
                    var desc = (string?)obj?["desc"];
                    var h1 = (string?)obj?["h1"];

                    var meta = new SelfMeta(
                        Collapse(title, 120),
                        Collapse(desc, 300),
                        Collapse(h1, 140)
                    );

                    return (visible, meta);
                }
                catch
                {
                    // Absolute final safety net: ExtractFromAsync must never throw.
                    return ("", new SelfMeta(null, null, null));
                }
            }

            static async Task BestEffortSettleAsync(IPage page, int maxWaitMs, CancellationToken ct)
            {
                // We stop early if:
                // - text length crosses a threshold, OR
                // - it stops increasing for a few polls (settled)
                const int minUseful = 300;
                const int pollMs = 250;
                const int stablePollsNeeded = 4; // ~1s stable

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
                    catch
                    {
                        // Execution context might not be ready; keep polling
                    }

                    if (len >= minUseful)
                        return;

                    if (len <= lastLen)
                        stable++;
                    else
                        stable = 0;

                    lastLen = len;

                    if (stable >= stablePollsNeeded)
                        return;

                    try { await Task.Delay(pollMs, ct); } catch { return; }
                }
            }



            var builder = new StringBuilder();
            var bestMeta = staticMeta; // seed with static

            // 1) Home — capture both text + meta
            var (homeVisible, homeMeta) = await ExtractFromAsync(page, url, captureMeta: true, timeoutMs: 30000, ct);
            if (!string.IsNullOrWhiteSpace(homeVisible)) builder.AppendLine(homeVisible);

            // Merge meta (keep any static values already present)
            if (bestMeta.Title is null) bestMeta = bestMeta with { Title = homeMeta.Title };
            if (bestMeta.Description is null) bestMeta = bestMeta with { Description = homeMeta.Description };
            if (bestMeta.PositioningH1 is null) bestMeta = bestMeta with { PositioningH1 = homeMeta.PositioningH1 };

            // 2) Secondary paths — just harvest more text (faster)
            if (builder.Length < minUsefulChars)
            {
                foreach (var path in new[] { "/features", "/pricing", "/about" })
                {
                    var extraUrl = CombineUrl(url, path);
                    var (vis, _) = await ExtractFromAsync(page, extraUrl, captureMeta: false, timeoutMs: 20000, ct);
                    if (!string.IsNullOrWhiteSpace(vis))
                    {
                        builder.Append('\n').Append(vis);
                        if (builder.Length >= minUsefulChars * 2) break;
                    }
                }
            }

            var visibleCombined = builder.Length > 0 ? builder.ToString() : staticText;

            // 3) Meta fallback via lightweight HTTP (optional last resort)
            if ((bestMeta.Title is null || bestMeta.Description is null) && !ct.IsCancellationRequested)
            {
                var _ = await TryFetchMetaAsync(url, ct); // keep as-is; optional parse if you want
            }

            return new BestContentResult(NormalizeText(visibleCombined), bestMeta);
        }


        // Not used currently
        /*
        // Returns the best visible text we can get for a URL.
        // - staticDoc: your already-fetched HtmlAgilityPack doc (can be null)
        // - remote: your RemoteBrowser wrapper (must be connected)
        // - minUsefulChars: threshold to decide whether to render / try fallbacks
        private async Task<string> GetBestVisibleTextAsync(
            string url,
            HtmlDocument? staticDoc,
            RemoteBrowser remote,
            int minUsefulChars = 1200,
            CancellationToken ct = default)
        {
            url = UrlHelper.GetHttpsUrl(url);

            // 1) Static HTML (cheap)
            var text = ExtractStaticVisibleText(staticDoc);
            if (text.Length >= minUsefulChars)
                return NormalizeText(text);

            // 2) Rendered homepage (Playwright, via remote)
            var homeRendered = await RenderVisibleTextAsync(remote, url, 30_000, ct);
            if (!string.IsNullOrWhiteSpace(homeRendered) && homeRendered.Length > text.Length)
                text = homeRendered;

            if (text.Length >= minUsefulChars)
                return NormalizeText(text);

            // 3) Secondary pages (often SSR with richer copy)
            var extraPaths = new[] { "/features", "/pricing", "/about" };
            foreach (var path in extraPaths)
            {
                var extraUrl = CombineUrl(url, path);
                var extraText = await RenderVisibleTextAsync(remote, extraUrl, 20_000, ct);
                if (!string.IsNullOrWhiteSpace(extraText))
                {
                    text += "\n" + extraText;
                    if (text.Length >= (minUsefulChars * 2)) break; // don't overdo it
                }
            }

            if (text.Length >= minUsefulChars)
                return NormalizeText(text);

            // 4) Meta & JSON-LD (last resort; fast)
            var meta = await TryFetchMetaAsync(url, ct);
            if (!string.IsNullOrWhiteSpace(meta))
                text += "\n" + meta;

            return NormalizeText(text);
        }*/

        // ------- Helpers -------

        private static string ExtractStaticVisibleText(HtmlDocument? doc)
        {
            if (doc?.DocumentNode == null) return string.Empty;

            var sb = new StringBuilder();
            var bodyText = doc.DocumentNode.SelectSingleNode("//body")?
                .DescendantsAndSelf()
                .Where(n => n.NodeType == HtmlNodeType.Text &&
                            n.ParentNode.Name is not ("script" or "style" or "noscript"))
                .Select(n => HtmlEntity.DeEntitize(n.InnerText))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Take(800);

            if (bodyText != null)
                foreach (var t in bodyText) sb.AppendLine(t);

            return sb.ToString();
        }

        // not used currently
        private static async Task<string> RenderVisibleTextAsync(
            RemoteBrowser remote,
            string targetUrl,
            int timeoutMs,
            CancellationToken ct)
        {
            // New isolated context per job
            await using var context = await remote.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123 Safari/537.36",
                Locale = "en-US",
                ViewportSize = new() { Width = 1366, Height = 900 },
                ServiceWorkers = ServiceWorkerPolicy.Block
            });

            var page = await context.NewPageAsync();

            // Block heavy/noisy resources
            await page.RouteAsync("**/*", async route =>
            {
                var t = route.Request.ResourceType;      // "image","font","media","eventsource","websocket",...
                var u = route.Request.Url;
                if (t is "image" or "font" or "media" or "eventsource" or "websocket"
                    || u.Contains("hotjar") || u.Contains("fullstory") || u.Contains("intercom"))
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            });

            // Navigate with a resilient strategy
            try
            {
                // 1) Try fast-path: NetworkIdle (short timeout)
                await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    Timeout = Math.Min(timeoutMs, 10_000),
                    WaitUntil = WaitUntilState.NetworkIdle
                });
            }
            catch (Exception ex) when (ex is System.TimeoutException || ex is PlaywrightException)
            {
                await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    Timeout = timeoutMs,
                    WaitUntil = WaitUntilState.Load
                });
            }


            // Ensure there's a body
            await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 5_000 });

            // Optional: short "soft idle" (ignore chatty sockets)
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 2_000 });
            }
            catch { /* still chatty, that's fine */ }

            // Optional: ensure there's some actual text before we extract
            try
            {
                await page.WaitForFunctionAsync(
                    @"() => (document.body.innerText || '').trim().length > 300",
                    null, new PageWaitForFunctionOptions { Timeout = 3_000 });
            }
            catch { /* minimal text is okay; proceed */ }

            // Visible-text extractor in the page context
            var visible = await page.EvaluateAsync<string>(
            @"() => {
                const reject = new Set(['SCRIPT','STYLE','NOSCRIPT','IFRAME','SVG','CANVAS','TEMPLATE']);
                const isHidden = (el) => {
                  const cs = window.getComputedStyle(el);
                  return cs.display==='none' || cs.visibility==='hidden' || el.getAttribute('aria-hidden')==='true';
                };
                const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                  acceptNode: n => {
                    const p = n.parentElement;
                    if (!p || reject.has(p.tagName) || isHidden(p)) return NodeFilter.FILTER_REJECT;
                    const t = n.nodeValue?.replace(/\s+/g,' ').trim();
                    return t ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
                  }
                });
                const out = [];
                while (walker.nextNode()) out.push(walker.currentNode.nodeValue.replace(/\s+/g,' ').trim());
                const dedup = Array.from(new Set(out.filter(s => s.length >= 3)));
                return dedup.join('\n');
            }");

            return visible ?? string.Empty;
        }


        private static async Task<string> TryFetchMetaAsync(string url, CancellationToken ct)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                using var resp = await http.GetAsync(url, ct);
                var html = await resp.Content.ReadAsStringAsync(ct);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var head = doc.DocumentNode.SelectSingleNode("//head");
                var og = head?.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", null);
                var md = head?.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", null);
                var ld = head?.SelectNodes("//script[@type='application/ld+json']")?
                             .Select(n => n.InnerText)
                             .Where(s => !string.IsNullOrWhiteSpace(s))
                             .ToList() ?? new List<string>();
                // keep ld+json short
                var ldJoined = string.Join("\n", ld.Select(s => s.Length > 800 ? s[..800] : s));
                return string.Join("\n", new[] { og, md, ldJoined }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            catch { return string.Empty; }
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return baseUrl;
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            return new Uri(new Uri(baseUrl), path.TrimStart('/')).ToString();
        }

        private static string NormalizeText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            // collapse runs, trim, cap size to keep token costs sane
            var norm = Regex.Replace(s, @"[ \t]+", " ");
            norm = Regex.Replace(norm, @"\n{3,}", "\n\n").Trim();
            if (norm.Length > 8000) norm = norm[..8000];
            return norm;
        }


        private static async Task<SelfMeta> ExtractSelfMetaFromRenderedAsync(
            RemoteBrowser remote, string url, int timeoutMs = 15000, CancellationToken ct = default)
        {
            await using var context = await remote.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123 Safari/537.36",
                Locale = "en-US",
                ViewportSize = new() { Width = 1366, Height = 900 },
                ServiceWorkers = ServiceWorkerPolicy.Block
            });
            var page = await context.NewPageAsync();

            await page.RouteAsync("**/*", async route =>
            {
                var t = route.Request.ResourceType;
                if (t is "image" or "font" or "media" or "eventsource" or "websocket")
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            });

            try
            {
                await page.GotoAsync(url, new PageGotoOptions { Timeout = timeoutMs, WaitUntil = WaitUntilState.Load });
                await page.WaitForSelectorAsync("body", new() { Timeout = 5000 });
            }
            catch { /* best effort */ }

            var json = await page.EvaluateAsync<string>(
            @"() => {
                const by = (sel) => document.querySelector(sel);
                const getMeta = (n) => {
                  const m = document.querySelector(`meta[name='${n}']`) || document.querySelector(`meta[property='${n}']`);
                  return m?.getAttribute('content') || null;
                };
                const title = document.title || null;
                const desc = getMeta('description') || getMeta('og:description');
                const h1 = (by('h1')?.innerText || '').trim() || null;
                return JSON.stringify({ title, desc, h1 });
            }");

            var o = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json!);
            return new(
                Collapse(o?["title"], 120),
                Collapse(o?["desc"], 300),
                Collapse(o?["h1"], 140)
            );
        }


        private static string BuildSelfBlock(SelfMeta m)
        {
            var lines = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(m.Title)) lines.Add($"SELF_TITLE: {m.Title}");
            if (!string.IsNullOrWhiteSpace(m.Description)) lines.Add($"SELF_DESCRIPTION: {m.Description}");
            if (!string.IsNullOrWhiteSpace(m.PositioningH1)) lines.Add($"SELF_POSITIONING: {m.PositioningH1}");
            return string.Join('\n', lines);
        }

        private static string? Collapse(string? s, int max = 200)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var t = Regex.Replace(s, @"\s+", " ").Trim();
            return t.Length > max ? t[..max] : t;
        }

        private static SelfMeta ExtractSelfMetaFromStatic(HtmlDocument? doc)
        {
            if (doc?.DocumentNode is null) return new(null, null, null);

            // <title>
            var title = doc.DocumentNode.SelectSingleNode("//head/title")?.InnerText;

            // meta description (name=description), fallback og:description
            string? desc = null;
            var metas = doc.DocumentNode.SelectNodes("//head/meta") ?? new HtmlNodeCollection(null);
            foreach (var m in metas)
            {
                var name = (m.GetAttributeValue("name", null) ?? m.GetAttributeValue("property", null))?.ToLowerInvariant();
                if (name is "description" or "og:description")
                {
                    desc = m.GetAttributeValue("content", null);
                    if (!string.IsNullOrWhiteSpace(desc)) break;
                }
            }

            // first visible H1
            var h1 = ExtractCleanH1(doc);
            return new SelfMeta(
                Collapse(title, 120),
                Collapse(desc, 300),
                Collapse(h1, 140)
            );

        }

        private static string? ExtractCleanH1(HtmlDocument doc)
        {
            var h1 = doc.DocumentNode.SelectSingleNode("//body//h1[normalize-space()]");
            if (h1 is null) return null;

            // Match common typewriter/typed widgets by data-* or class names (case-insensitive)
            var twNodes = h1.SelectNodes(
                ".//*[" +
                "@data-strings or @data-words or @data-rotate or @data-type or " +
                "contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'typewriter') or " +
                "contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'typed') or " +
                "contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'typewrite')" +
                "]"
            ) ?? new HtmlNodeCollection(h1);

            foreach (var n in twNodes)
            {
                // Pull raw config from data-* (prefer data-strings, with fallbacks)
                var raw = n.GetAttributeValue("data-strings",
                          n.GetAttributeValue("data-words",
                          n.GetAttributeValue("data-rotate",
                          n.GetAttributeValue("data-type", null))));

                var variants = new List<string>();

                // 1) Try JSON array (after HTML decode)
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var decoded = System.Net.WebUtility.HtmlDecode(raw).Trim();

                    try
                    {
                        var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(decoded);
                        if (arr is not null)
                            variants.AddRange(arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                    }
                    catch
                    {
                        // 2) Try quoted substrings
                        foreach (System.Text.RegularExpressions.Match m in
                                 System.Text.RegularExpressions.Regex.Matches(decoded, @"""([^""]+)""|'([^']+)'"))
                        {
                            var v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                            if (!string.IsNullOrWhiteSpace(v)) variants.Add(v.Trim());
                        }

                        // 3) Fallback: strip brackets then split on common separators
                        if (variants.Count == 0)
                        {
                            var stripped = System.Text.RegularExpressions.Regex
                                .Replace(decoded, @"^[\[\(\{]+|[\]\)\}]+$", string.Empty);
                            variants.AddRange(
                                stripped.Split(new[] { '|', ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim())
                            );
                        }
                    }
                }

                // 4) Last resort: harvest child texts (measurement/helper spans etc.)
                if (variants.Count == 0)
                {
                    variants.AddRange(
                        n.Descendants()
                         .Where(d => d.NodeType == HtmlAgilityPack.HtmlNodeType.Text)
                         .Select(t => HtmlEntity.DeEntitize(t.InnerText).Trim())
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                    );
                }

                // Clean stray quotes, dedupe (case-insensitive), keep it reasonable
                variants = variants
                    .Select(v => System.Text.RegularExpressions.Regex.Replace(v, @"^['""“”]+|['""“”]+$", "").Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList();

                // Only replace if we truly have alternatives
                if (variants.Count > 1)
                {
                    var joined = string.Join(" / ", variants);
                    n.InnerHtml = System.Net.WebUtility.HtmlEncode(joined);
                }
            }

            var text = HtmlEntity.DeEntitize(h1.InnerText);
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }


        public async Task<string> GetCompanyNameFromTitle(string title, string domain)
        {
            // Step 1: Split and extract a potential company name
            string extractedName = ExtractCompanyName(title, domain);

            // Step 2: Remove marketing words
            extractedName = CleanCompanyName(extractedName);

            // Step 3: Verify with domain name
            extractedName = ValidateCompanyNameWithDomain(extractedName, domain);

            return extractedName;
        }

        public string ExtractCompanyName(string title, string domain)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;

            var parts = title.Split(new[] { "|", "-", "•", ":", "·" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (parts.Count == 0) return title;

            // Try match with domain (e.g., databox.com -> databox)
            var domainName = domain.Split('.')[0].ToLowerInvariant().Replace("https://", "").Replace("http://", ""); ;

            var matchedPart = parts.FirstOrDefault(p =>
                p.Replace(" ", "").ToLowerInvariant().Contains(domainName));

            if (!string.IsNullOrEmpty(matchedPart))
                return matchedPart;

            // Fallback to first part
            return parts.First();
        }

        public string CleanCompanyName(string name)
        {
            // List of words that are NOT part of a company name
            string[] marketingWords = { "Best", "Top", "Affordable", "Software", "Platform", "Solutions",
                                "Cloud", "AI", "CRM", "SaaS", "Tools", "Technology", "Tech",
                                "For", "Marketing", "Automation", "Dashboard", "Analytics",
                                "SEM", "SEO", "Security", "Startups", "Business", "Enterprise", "ReCaptcha" };

            // Remove words from the beginning of the string
            var words = name.Split(' ').ToList();
            words.RemoveAll(word => marketingWords.Contains(word, StringComparer.OrdinalIgnoreCase));

            return string.Join(" ", words).Trim();
        }

        public string ValidateCompanyNameWithDomain(string extractedName, string domain)
        {
            if (string.IsNullOrEmpty(domain)) return extractedName;

            // Get main part of the domain (removes ".com", ".net", etc.)
            string cleanDomain = Regex.Replace(domain, @"(https?://|www\.)", "", RegexOptions.IgnoreCase).Split('.')[0];

            cleanDomain = Regex.Replace(cleanDomain, @"(?<=\w)(get|app|hq|tech|ai|co|my|the|go|try|on)(?=\b|$)", "", RegexOptions.IgnoreCase);

            // Trim any accidental leading or trailing hyphens left after the removal
            cleanDomain = cleanDomain.Trim('-');

            // If the cleaned name results in **multiple words**, revert to the original extractedName
            if (string.IsNullOrWhiteSpace(extractedName)
                || extractedName.Split(' ').Length > 2
                || extractedName.Length < 3)
            {
                extractedName = cleanDomain;
            }


            // ✅ Capitalize the first letter
            return char.ToUpper(extractedName[0]) + extractedName.Substring(1); ;
        }

        public string ExtractDomainFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";

            // ✅ Remove "https://", "http://", "www." and get only the root domain
            string cleanDomain = Regex.Replace(url, @"(https?://|www\.)", "", RegexOptions.IgnoreCase)
                .Split('/')[0]; // ✅ Removes everything after "/"

            return cleanDomain;
        }

        public async Task<Dictionary<int, DataSourceType>> GetSocialPlatformsAsync()
        {
            if (_socialPlatforms != null && _socialPlatforms.Count > 0)
                return _socialPlatforms;

            _loadTask ??= LoadPlatformsAsync(); // ✅ Start loading if not already started
            _socialPlatforms = await _loadTask;

            return _socialPlatforms;
        }

        private async Task<Dictionary<int, DataSourceType>> LoadPlatformsAsync(CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.DataSourceTypes
                .AsNoTracking()
                .ToDictionaryAsync(t => t.Id, ct);
        }

        public async Task<List<string>> ExtractSocialAndBlogLinks(HtmlDocument doc, string baseUrl)
        {
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var allowedTypeIds = new[]
            {
                (int)DataSourceTypeEnum.Facebook,
                (int)DataSourceTypeEnum.Instagram,
                (int)DataSourceTypeEnum.Youtube,
                (int)DataSourceTypeEnum.Linkedin,
                (int)DataSourceTypeEnum.X,
                (int)DataSourceTypeEnum.TikTok,
                (int)DataSourceTypeEnum.Blog,
                (int)DataSourceTypeEnum.News,
                (int)DataSourceTypeEnum.EmailNewsletters,
                (int)DataSourceTypeEnum.Reddit
            };
            var types = (await GetSocialPlatformsAsync())
                .Where(kv => allowedTypeIds.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var hostKeywords = types.Values
                .Where(t => !string.IsNullOrWhiteSpace(t.UrlKeywords))
                .SelectMany(t => t.UrlKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Select(k => k.Trim())
                .Where(k => k.Contains('.'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var pathKeywords = types.Values
                .Where(t => !string.IsNullOrWhiteSpace(t.UrlKeywords))
                .SelectMany(t => t.UrlKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Select(k => k.Trim().Trim('/'))
                .Where(k => k.Length > 0 && !k.Contains('.'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            bool IsRelevant(string url)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

                if (hostKeywords.Any(h => HostMatches(uri.Host, h)))
                    return true;

                var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return segs.Any(s => pathKeywords.Any(p => s.Equals(p, StringComparison.OrdinalIgnoreCase)));
            }

            // 1) Normal anchors
            var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchors != null)
            {
                foreach (var a in anchors)
                {
                    var href = a.GetAttributeValue("href", "").Trim();
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    href = NormalizeUrl(href, baseUrl);

                    if (IsRelevant(href))
                        urls.Add(href);
                }
            }

            // 2) JS/JSON embedded hrefs (like "link":{"href":"/blog"})
            foreach (var href in ExtractJsEmbeddedLinks(doc.DocumentNode.OuterHtml, baseUrl))
            {
                if (IsRelevant(href))
                    urls.Add(href);
            }

            // 2.5) JSON-LD sameAs links
            foreach (var u in ExtractJsonLdSameAsLinks(doc, baseUrl))
            {
                if (IsRelevant(u))
                    urls.Add(u);
            }


            return urls.ToList();
        }

        static bool HostMatches(string host, string keyword)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(keyword))
                return false;

            host = host.Trim().TrimEnd('.');                 // normalize
            keyword = keyword.Trim().TrimStart('.').TrimEnd('.');

            return host.Equals(keyword, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + keyword, StringComparison.OrdinalIgnoreCase);
        }


        private IEnumerable<string> ExtractJsEmbeddedLinks(string html, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(html))
                yield break;

            // Matches:
            //   href:"/blog"
            //   "href":"/blog"
            //   \"href\":\"/blog\"
            //   href\":\"/blog\"   (common inside JSON strings)
            var rx = new System.Text.RegularExpressions.Regex(
                @"href\s*\\?[""']\s*:\s*\\?[""'](?<u>[^""']{1,600})\\?[""']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled);

            foreach (System.Text.RegularExpressions.Match m in rx.Matches(html))
            {
                var raw = m.Groups["u"].Value.Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                // Decode common JSON/string escapes
                raw = raw.Replace("\\/", "/");                 // JSON escaped slashes
                raw = System.Text.RegularExpressions.Regex.Unescape(raw); // \" -> ", \u002f, etc.

                // Skip obvious non-links
                if (raw.StartsWith("#")) continue;
                if (raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
                if (raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                if (raw.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) continue;

                yield return NormalizeUrl(raw, baseUrl);
            }
        }

        private IEnumerable<string> ExtractJsonLdSameAsLinks(HtmlDocument doc, string baseUrl)
        {
            var nodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (nodes == null) yield break;

            foreach (var n in nodes)
            {
                var json = n.InnerText?.Trim();
                if (string.IsNullOrWhiteSpace(json)) continue;

                JsonDocument? jdoc = null;
                try { jdoc = JsonDocument.Parse(json); }
                catch { continue; }

                using (jdoc)
                {
                    foreach (var u in ExtractSameAsFromElement(jdoc.RootElement))
                    {
                        if (string.IsNullOrWhiteSpace(u)) continue;
                        yield return NormalizeUrl(u, baseUrl);
                    }
                }
            }
        }

        private IEnumerable<string> ExtractSameAsFromElement(JsonElement el)
        {
            // JSON-LD can be:
            //  - object with sameAs
            //  - array of objects
            //  - graph { "@graph": [...] }
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    foreach (var u in ExtractSameAsFromElement(item))
                        yield return u;
                yield break;
            }

            if (el.ValueKind != JsonValueKind.Object)
                yield break;

            // handle @graph
            if (el.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in graph.EnumerateArray())
                    foreach (var u in ExtractSameAsFromElement(item))
                        yield return u;
            }

            if (!el.TryGetProperty("sameAs", out var sameAs))
                yield break;

            if (sameAs.ValueKind == JsonValueKind.String)
            {
                yield return sameAs.GetString()!;
            }
            else if (sameAs.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sameAs.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.String)
                        yield return s.GetString()!;
                }
            }
        }

        bool HasEnoughSocials(IEnumerable<string> xs)
        {
            // You decide threshold; 2 is usually enough for onboarding
            var socials = xs.Count(IsRecognizedSocial);
            return socials >= 2;
        }

        bool IsRecognizedSocial(string x)
        {
            x = x ?? "";
            return x.Contains("linkedin.com/", StringComparison.OrdinalIgnoreCase)
                || x.Contains("x.com/", StringComparison.OrdinalIgnoreCase)
                || x.Contains("twitter.com/", StringComparison.OrdinalIgnoreCase)
                || x.Contains("facebook.com/", StringComparison.OrdinalIgnoreCase)
                || x.Contains("instagram.com/", StringComparison.OrdinalIgnoreCase)
                || x.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase)
                || x.Contains("reddit.com/", StringComparison.OrdinalIgnoreCase)
                || x.Contains("tiktok.com/", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<List<string>> ExtractSocialAndBlogLinksWithRenderedFallbackAsync(
            string url,
            CancellationToken ct = default)
        {
            // 1) Raw (cheap)
            var rawHtml = await _brightDataService.FetchHtmlAsync(url, ct);
            if (string.IsNullOrWhiteSpace(rawHtml))
                return new();

            var doc1 = new HtmlDocument();
            doc1.LoadHtml(rawHtml);

            var links = await ExtractSocialAndBlogLinks(doc1, url);

            // If we already got LinkedIn, stop
            if (HasEnoughSocials(links))
                return links;

            // 2) Rendered (expensive) fallback
            var rendered = await _pageContentFetchService.FetchRenderedHtmlAsync(url, ct);

            // If render fails, keep raw result
            if (rendered == null || !rendered.Ok)
                return links;

            var combined = new HashSet<string>(links, StringComparer.OrdinalIgnoreCase);

            // ✅ Prefer DOM-extracted social links (most reliable for hydrated footers)
            if (rendered.SocialLinks is { Count: > 0 })
            {
                foreach (var s in rendered.SocialLinks)
                {
                    var norm = NormalizeUrl(s, url);
                    if (!string.IsNullOrWhiteSpace(norm))
                        combined.Add(norm);
                }
            }

            // If we now have LinkedIn, we can stop early
            var combinedList = combined.ToList();
            if (HasEnoughSocials(combinedList))
                return combinedList;

            // 🧯 Backup: parse rendered HTML too (sometimes DOM list misses weird cases)
            if (!string.IsNullOrWhiteSpace(rendered.Html))
            {
                var doc2 = new HtmlDocument();
                doc2.LoadHtml(rendered.Html);

                var links2 = await ExtractSocialAndBlogLinks(doc2, url);
                foreach (var l in links2)
                    combined.Add(l);
            }

            return combined.ToList();
        }

        public string NormalizeUrl(string href, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(href))
                return "";

            href = href.Trim();

            // Skip obvious non-links early
            if (href.StartsWith("#")) return "";
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return "";
            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return "";
            if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return "";

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
                return "";

            // Protocol-relative -> force https
            if (href.StartsWith("//"))
                href = $"https:{href}";

            // Absolute URL?
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
            {
                // Only accept http/https; force https
                if (abs.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                    abs.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    return CleanForceHttps(abs);
                }

                return ""; // reject other schemes
            }

            // Relative -> resolve against base, then force https
            if (Uri.TryCreate(baseUri, href, out var resolved))
            {
                // Resolved might inherit base scheme (http/https). Force https anyway.
                if (resolved.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                    resolved.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    return CleanForceHttps(resolved);
                }

                return "";
            }

            return "";

            static string CleanForceHttps(Uri uri)
            {
                // Normalize to https + remove fragment + strip tracking (non-social)
                var b = new UriBuilder(uri)
                {
                    Scheme = Uri.UriSchemeHttps,
                    Port = -1,   // default port (443)
                    Fragment = "" // remove anchors
                };

                var host = b.Host ?? "";
                var isSocial =
                    host.EndsWith("x.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("twitter.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("linkedin.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("facebook.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("instagram.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("reddit.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("tiktok.com", StringComparison.OrdinalIgnoreCase);

                b.Query = isSocial ? "" : StripTrackingQuery(b.Query);

                // Ensure it’s a valid https URL
                if (!b.Uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    return "";

                return b.Uri.ToString().TrimEnd('/');
            }

            static string StripTrackingQuery(string query)
            {
                if (string.IsNullOrWhiteSpace(query))
                    return "";

                if (query.StartsWith("?"))
                    query = query[1..];

                var dropExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "fbclid","gclid","dclid","msclkid","igshid","mc_cid","mc_eid",
                    "ref","ref_src","ref_url","cmpid","sr","spm",
                    "lang","locale"
                };

                static bool ShouldDrop(string key, HashSet<string> dropExact)
                {
                    if (string.IsNullOrWhiteSpace(key)) return false;

                    if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) return true;
                    if (key.StartsWith("ga_", StringComparison.OrdinalIgnoreCase)) return true;
                    if (key.StartsWith("vero_", StringComparison.OrdinalIgnoreCase)) return true;
                    if (key.StartsWith("hs_", StringComparison.OrdinalIgnoreCase)) return true;
                    if (key.StartsWith("mkt_", StringComparison.OrdinalIgnoreCase)) return true;
                    if (key.StartsWith("_hs", StringComparison.OrdinalIgnoreCase)) return true;

                    return dropExact.Contains(key);
                }

                var kept = new List<string>();
                foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var idx = part.IndexOf('=');
                    var key = idx >= 0 ? part[..idx] : part;
                    key = Uri.UnescapeDataString(key.Replace('+', ' '));

                    if (!ShouldDrop(key, dropExact))
                        kept.Add(part);
                }

                return kept.Count == 0 ? "" : string.Join("&", kept);
            }
        }

        public async Task SaveSocialLinksAsync(int companyId, List<string> socialLinks)
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            // ✅ Use cached data instead of querying the DB again
            var socialPlatforms = await GetSocialPlatformsAsync();

            foreach (var link in socialLinks)
            {
                string fullUrl = link.ToLowerInvariant();
                string domain = new Uri(link).Host.Replace("www.", "").ToLower();

                _logger.LogInformation("fullUrl={FullUrl}", fullUrl);

                // First try domain-based match
                var dataSourceType = socialPlatforms.Values.FirstOrDefault(type =>
                    type.UrlKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Any(keyword => domain.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

                // If not found, try full URL (for blogs)
                if (dataSourceType == null)
                {
                    dataSourceType = socialPlatforms.Values.FirstOrDefault(type =>
                        type.UrlKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Any(keyword => fullUrl.Contains($"/{keyword}", StringComparison.OrdinalIgnoreCase)
                                     || fullUrl.Contains($"{keyword}.", StringComparison.OrdinalIgnoreCase))); // matches blog.domain.com
                }

                if (dataSourceType == null)
                {
                    Console.WriteLine($"Skipping unknown platform: {domain}");
                    continue;
                }

                _logger.LogInformation("Detect DS type for {Url} -> {Type}", fullUrl, dataSourceType?.Name);

                // Special case: filter out blog articles (keep only blog root)
                var typeId = dataSourceType.Id; // or dataSourceType.TypeId depending on your model

                var isBlogOrNews =
                    typeId == (int)DataSourceTypeEnum.Blog ||
                    typeId == (int)DataSourceTypeEnum.News;

                if (isBlogOrNews && !IsLikelyBlogHomepage(link))
                {
                    Console.WriteLine($"⛔ Skipping article-level link: {link}");
                    continue;
                }

                // ✅ Prevent duplicates: Check if this URL already exists for this company
                bool exists = await dbContext.DataSources
                    .AnyAsync(ds => ds.Url == link && ds.CompanyId == companyId);

                if (!exists)
                {
                    var newDataSource = new DataSource
                    {
                        Url = link,
                        CompanyId = companyId,
                        TypeId = dataSourceType.Id,
                        DateCreated = DateTime.UtcNow,
                        IsActive = true
                    };

                    dbContext.DataSources.Add(newDataSource);
                }
            }

            await dbContext.SaveChangesAsync(); // ✅ Save everything in one batch
        }

        bool IsLikelyBlogHomepage(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return false;

                var host = uri.Host.ToLowerInvariant();
                var path = uri.AbsolutePath.ToLowerInvariant().TrimEnd('/');

                // blog.company.com (root only)
                if (host.StartsWith("blog.") && (path == "" || path == "/"))
                    return true;

                // Accept exact root paths
                var exact = new HashSet<string>
                {
                    "/blog", "/news", "/insights", "/press", "/articles", "/press-releases",
                    "/newsroom", "/changelog", "/updates"
                };
                if (exact.Contains(path))
                    return true;

                // Accept nested "section homepages" (e.g., /company/newsroom, /about/press)
                var lastSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (lastSegment is null) return false;

                var allowedLastSegments = new HashSet<string>
                {
                    "blog", "news", "newsroom", "press", "media", "announcements",
                    "insights", "articles", "changelog", "updates", "releases"
                };

                return allowedLastSegments.Contains(lastSegment);
            }
            catch
            {
                return false;
            }
        }

        public async Task MarkFacebookReviewsAsUnavailableAsync(int companyId)
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var company = await dbContext.Companies.FindAsync(companyId);
            if (company == null)
            {
                _logger.LogWarning($"Company not found for ID: {companyId}");
                return;
            }

            if (company.HasFacebookReviews == false)
            {
                _logger.LogInformation($"Company {company.Name} (ID: {companyId}) already marked as having no Facebook reviews.");
                return;
            }

            company.HasFacebookReviews = false;
            await dbContext.SaveChangesAsync();

            _logger.LogInformation($"Marked company {company.Name} (ID: {companyId}) as having no Facebook reviews.");
        }

        private async Task<(List<string> hostKeywords, List<string> pathKeywords)> GetKeywordMatchersAsync()
        {
            var types = await GetSocialPlatformsAsync();

            var all = types.Values
                .Where(t => !string.IsNullOrWhiteSpace(t.UrlKeywords))
                .SelectMany(t => t.UrlKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Heuristic:
            // - contains a dot => host keyword (linkedin.com, blog., etc.)
            // - otherwise => path keyword (blog, newsroom, press-releases, etc.)
            var host = all.Where(k => k.Contains('.')).ToList();
            var path = all.Where(k => !k.Contains('.')).ToList();

            return (host, path);
        }

    }
}
