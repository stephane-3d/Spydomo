using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class FeedbackDataService : IFeedbackDataService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IBrightDataService _brightDataService;
        private readonly ISnapshotTrackerService _snapshotTrackerService;
        private readonly GoogleSearchService _googleSearchService;
        private readonly FeedbackParserFactory _parserFactory;
        private readonly DbDataService _dbDataService;
        private readonly ILogger<FeedbackDataService> _logger;

        public FeedbackDataService(
            IDbContextFactory<SpydomoContext> dbFactory,
            IBrightDataService brightDataService,
            ISnapshotTrackerService snapshotTrackerService,
            FeedbackParserFactory parserFactory,
            GoogleSearchService googleSearchService,
            DbDataService dbDataService,
            ILogger<FeedbackDataService> logger)
        {
            _dbFactory = dbFactory;
            _brightDataService = brightDataService;
            _snapshotTrackerService = snapshotTrackerService;
            _parserFactory = parserFactory;
            _googleSearchService = googleSearchService;
            _dbDataService = dbDataService;
            _logger = logger;
        }


        public async Task FetchReviewsForCompany(int companyId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var company = await db.Companies
                .Include(c => c.DataSources)
                .FirstOrDefaultAsync(c => c.Id == companyId, ct);

            if (company == null)
                return;

            var now = DateTime.UtcNow;
            var refreshAfter = now.AddDays(-15);

            var toUpsert = new List<RawContent>();

            foreach (var dataSource in company.DataSources)
            {
                var sourceType = (DataSourceTypeEnum)dataSource.TypeId;

                // Temporary: only these sources
                if (sourceType != DataSourceTypeEnum.Capterra &&
                    sourceType != DataSourceTypeEnum.G2)
                    continue;

                // ✅ Hard gate: no BrightData call unless due
                if (dataSource.LastUpdate.HasValue && dataSource.LastUpdate.Value > refreshAfter)
                {
                    _logger.LogDebug("Skipping fetch (not due). CompanyId={CompanyId}, Type={Type}, Url={Url}, LastUpdate={LastUpdate:o}",
                        companyId, sourceType, dataSource.Url, dataSource.LastUpdate.Value);
                    continue;
                }

                try
                {
                    var parser = _parserFactory.GetParser(sourceType);
                    if (parser == null)
                        throw new NotSupportedException($"No parser found for {dataSource.TypeId}");

                    // ✅ Only called if due
                    var raw = await parser.FetchRawContentAsync(dataSource.Url, dataSource.LastUpdate);

                    // If BrightData returns no content, treat as "checked" (success)
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        _logger.LogInformation("No content returned (treated as up-to-date): {Url} Company={CompanyName} (ID:{CompanyId})",
                            dataSource.Url, company.Name, company.Id);

                        dataSource.LastUpdate = now;
                        continue;
                    }

                    // Snapshot triggered => "checked" (success), wait for webhook
                    if (IsSnapshotResponse(raw))
                    {
                        var snapshotId = ExtractSnapshotId(raw);
                        if (!string.IsNullOrWhiteSpace(snapshotId))
                        {
                            await _snapshotTrackerService.TrackAsync(
                                snapshotId,
                                companyId,
                                (int)dataSource.TypeId,
                                trackingData: null,
                                dateFilter: null);
                        }

                        _logger.LogInformation("BrightData snapshot triggered for {CompanyName} (ID:{CompanyId}), waiting for webhook.",
                            company.Name, company.Id);

                        dataSource.LastUpdate = now;
                        continue;
                    }

                    var parsed = await parser.Parse(raw, companyId, dataSource, dataSource.LastUpdate);

                    // Parsed can be empty if no new reviews since last update — still success
                    if (parsed is { Count: > 0 })
                        toUpsert.AddRange(parsed);

                    dataSource.LastUpdate = now;
                }
                catch (NotSupportedException ex)
                {
                    _logger.LogError(ex, "No parser found for DataSourceTypeId={TypeId}", dataSource.TypeId);
                    // do NOT update LastUpdate; fix needed
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FetchReviewsForCompany failed: CompanyId={CompanyId}, Url={Url}", companyId, dataSource.Url);
                    // do NOT update LastUpdate; retry next run
                }
            }

            await UpsertRawContentsAsync(db, companyId, toUpsert, ct);
            await db.SaveChangesAsync(ct);
        }

        // Public API stays the same, but now it batches and saves once.
        public async Task StoreReviewsAsync(int companyId, List<RawContent> reviews, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await UpsertRawContentsAsync(db, companyId, reviews, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task<int?> FindCompanyIdByUrlAsync(string url, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var dataSource = await db.DataSources
                .AsNoTracking()
                .FirstOrDefaultAsync(ds => ds.Url == url);

            return dataSource?.CompanyId;
        }

        public async Task FetchRedditMentionsForCompany(int companyId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var company = await db.Companies.FindAsync(companyId);
            if (company == null) return;

            var existingUrls = await db.RawContents
                .AsNoTracking()
                .Where(rc => rc.CompanyId == companyId && rc.DataSourceTypeId == (int)DataSourceTypeEnum.Reddit)
                .Select(rc => rc.PostUrl)
                .ToListAsync();

            var existingUrlSet = new HashSet<string>(
                existingUrls.Where(u => !string.IsNullOrWhiteSpace(u))!,
                StringComparer.OrdinalIgnoreCase);

            var filteredUrls = await db.FilteredUrls
                .AsNoTracking()
                .Where(f => f.CompanyId == companyId && f.SourceTypeId == (int)DataSourceTypeEnum.Reddit)
                .Select(f => f.PostUrl)
                .ToListAsync();

            var filteredSet = new HashSet<string>(
                filteredUrls.Where(u => !string.IsNullOrWhiteSpace(u))!,
                StringComparer.OrdinalIgnoreCase);

            var results = await _googleSearchService.SearchAsync(
                company.Name,
                new[] { company.Name, company.Url },
                "reddit.com/r/");

            var newUrls = results
                .Select(r => (r.Url ?? "").Trim().TrimEnd('/'))
                .Where(u => u.Contains("/comments/", StringComparison.OrdinalIgnoreCase))
                .Where(u => !existingUrlSet.Contains(u) && !filteredSet.Contains(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!newUrls.Any())
                return;

            var toUpsert = new List<RawContent>();

            foreach (var url in newUrls)
            {
                try
                {
                    var jsonUrl = BuildRedditJsonUrl(url, limit: 20, depth: 1);
                    var fullPostJson = await _brightDataService.FetchRedditJsonAsync(jsonUrl, ct);

                    if (string.IsNullOrWhiteSpace(fullPostJson))
                        continue;

                    var parser = _parserFactory.GetParser(DataSourceTypeEnum.Reddit);
                    if (parser is null)
                        throw new InvalidOperationException("Reddit parser is not registered.");

                    var feedback = await parser.Parse(fullPostJson, companyId, null, company.LastRedditLookup);

                    if (feedback.Any())
                    {
                        toUpsert.AddRange(feedback);
                    }
                    else
                    {
                        // NOTE: If DbDataService saves internally, this is still extra I/O.
                        // Next optimization would be: add FilteredUrl entities here and SaveChanges once.
                        await _dbDataService.AddFilteredUrlAsync(companyId, url, (int)DataSourceTypeEnum.Reddit, "NotRelevant");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching Reddit mention for CompanyId={CompanyId}, Url={Url}", companyId, url);
                }

                await Task.Delay(1250, ct);
            }

            // ✅ One batched upsert + one save
            await UpsertRawContentsAsync(db, companyId, toUpsert, ct);

            company.LastRedditLookup = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        private static string BuildRedditJsonUrl(string postUrl, int limit = 20, int depth = 1)
        {
            // Remove query string and trailing slash
            var baseUrl = postUrl.Split('?', 2)[0].TrimEnd('/');

            // If someone already gave a .json URL, normalize it
            if (baseUrl.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return $"{baseUrl}?limit={limit}&depth={depth}";

            // Reddit expects .json on the PATH
            return $"{baseUrl}.json?limit={limit}&depth={depth}";
        }

        public async Task FetchLinkedInMentionsForCompany(int companyId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var company = await db.Companies.FindAsync(companyId);
            if (company == null) return;

            var existingUrls = await db.RawContents
                .AsNoTracking()
                .Where(rc => rc.CompanyId == companyId && rc.DataSourceTypeId == (int)DataSourceTypeEnum.Linkedin)
                .Select(rc => rc.PostUrl)
                .ToListAsync();

            var existingUrlSet = new HashSet<string>(
                existingUrls.Where(u => !string.IsNullOrWhiteSpace(u))!,
                StringComparer.OrdinalIgnoreCase);

            var filteredUrls = await db.FilteredUrls
                .AsNoTracking()
                .Where(f => f.CompanyId == companyId && f.SourceTypeId == (int)DataSourceTypeEnum.Linkedin)
                .Select(f => f.PostUrl)
                .ToListAsync();

            var filteredSet = new HashSet<string>(
                filteredUrls.Where(u => !string.IsNullOrWhiteSpace(u))!,
                StringComparer.OrdinalIgnoreCase);

            var officialLinkedInUrls = await db.DataSources
                .AsNoTracking()
                .Where(ds => ds.CompanyId == companyId && ds.TypeId == (int)DataSourceTypeEnum.Linkedin)
                .Select(ds => ds.Url)
                .ToListAsync();

            var officialSlugs = officialLinkedInUrls
                .Select(GetLinkedInCompanySlug)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();


            var results = await _googleSearchService.SearchAsync(
                company.Name,
                new[] { company.Name, company.Url },
                "linkedin.com/posts");

            var newUrls = new List<string>();

            foreach (var r in results)
            {
                var url = (r.Url ?? "").Trim();
                if (string.IsNullOrWhiteSpace(url)) continue;

                if (!url.Contains("/posts/", StringComparison.OrdinalIgnoreCase) &&
                    !url.Contains("/feed/update/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var actorSlug = GetLinkedInPostActorSlug(url);

                var isCompanyGenerated =
                    actorSlug != null &&
                    officialSlugs.Any(os => os.Equals(actorSlug, StringComparison.OrdinalIgnoreCase));

                if (isCompanyGenerated) continue;
                if (existingUrlSet.Contains(url) || filteredSet.Contains(url)) continue;

                newUrls.Add(url);
            }

            if (!newUrls.Any())
                return;

            try
            {
                const string datasetId = "gd_lyy3tktm25m4avu764";

                var payload = newUrls
                    .Select(url => new Dictionary<string, string> { { "url", url } })
                    .ToList();

                var response = await _brightDataService.TriggerUrlScrapingAsync(datasetId, payload);
                var snapshotId = ExtractSnapshotId(response);

                if (!string.IsNullOrWhiteSpace(snapshotId))
                {
                    await _snapshotTrackerService.TrackAsync(
                        snapshotId,
                        companyId,
                        (int)DataSourceTypeEnum.Linkedin,
                        JsonSerializer.Serialize(newUrls),
                        "N/A",
                        OriginTypeEnum.UserGenerated);
                }

                company.LastLinkedinLookup = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FetchLinkedInMentionsForCompany failed for CompanyId={CompanyId}", companyId);
            }
        }

        private static string? GetLinkedInCompanySlug(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;

            if (!uri.Host.EndsWith("linkedin.com", StringComparison.OrdinalIgnoreCase))
                return null;

            var segs = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // /company/agencyanalytics
            var companyIdx = Array.FindIndex(segs, s => s.Equals("company", StringComparison.OrdinalIgnoreCase));
            if (companyIdx >= 0 && companyIdx + 1 < segs.Length)
                return segs[companyIdx + 1].Trim().ToLowerInvariant();

            // Some variants exist, but company slug is usually here
            return null;
        }

        private static string? GetLinkedInPostActorSlug(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;

            if (!uri.Host.EndsWith("linkedin.com", StringComparison.OrdinalIgnoreCase))
                return null;

            var segs = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // /posts/agencyanalytics_celebrating-...
            if (segs.Length >= 2 && segs[0].Equals("posts", StringComparison.OrdinalIgnoreCase))
            {
                var first = segs[1]; // agencyanalytics_celebrating-...
                var slug = first.Split('_', StringSplitOptions.RemoveEmptyEntries)[0];
                return slug.Trim().ToLowerInvariant();
            }

            // /feed/update/urn:li:activity:...
            // Can't infer actor slug from URL alone; handle elsewhere if needed.
            return null;
        }

        public async Task FetchFacebookReviewsAsync(int companyId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var company = await db.Companies.FindAsync(companyId);
            if (company == null || company.HasFacebookReviews == false)
                return;

            try
            {
                var facebookUrls = await db.DataSources
                     .AsNoTracking()
                     .Where(ds => ds.CompanyId == companyId && ds.TypeId == (int)DataSourceTypeEnum.Facebook)
                     .Select(ds => ds.Url)
                     .Distinct()
                     .ToListAsync();

                if (!facebookUrls.Any())
                    return;

                const string facebookReviewsDatasetId = "gd_m0dtqpiu1mbcyc2g86";

                var payload = facebookUrls.Select(url =>
                {
                    var normalizedUrl = url.EndsWith("/reviews", StringComparison.OrdinalIgnoreCase)
                        ? url
                        : url.TrimEnd('/') + "/reviews";

                    return new Dictionary<string, string>
                {
                    { "url", normalizedUrl },
                    { "num_of_reviews", "20" }
                };
                }).ToList();

                var response = await _brightDataService.TriggerUrlScrapingAsync(facebookReviewsDatasetId, payload);
                var snapshotId = ExtractSnapshotId(response);

                if (!string.IsNullOrWhiteSpace(snapshotId))
                {
                    await _snapshotTrackerService.TrackAsync(
                        snapshotId,
                        companyId,
                        (int)DataSourceTypeEnum.FacebookReviews,
                        JsonSerializer.Serialize(facebookUrls),
                        "N/A",
                        OriginTypeEnum.UserGenerated);
                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FetchFacebookReviewsAsync failed for CompanyId={CompanyId}", companyId);
            }
            finally
            {
                company.LastFacebookReviewsLookup = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        // --------------------------
        // Batch upsert helpers
        // --------------------------

        private async Task UpsertRawContentsAsync(SpydomoContext db, int companyId, List<RawContent>? incoming, CancellationToken ct = default)
        {
            if (incoming == null || incoming.Count == 0)
                return;

            // sanitize + enforce CompanyId
            var cleaned = incoming
                .Where(r => !string.IsNullOrWhiteSpace(r.PostUrl))
                .Where(r => r.PostedDate.HasValue)
                .Select(r =>
                {
                    r.CompanyId = companyId;
                    r.PostUrl = r.PostUrl!.Trim();
                    return r;
                })
                .ToList();

            if (cleaned.Count == 0)
                return;

            // Pull existing rows for these urls in one go
            var urls = cleaned
                .Select(r => r.PostUrl!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existing = await db.RawContents
                .Where(rc => rc.CompanyId == companyId && urls.Contains(rc.PostUrl))
                .Select(rc => new
                {
                    rc.Id,
                    rc.PostUrl,
                    rc.PostedDate,
                    rc.Content
                })
                .ToListAsync(ct);

            // Build lookup by (url, postedDate truncated-to-second)
            // Build lookup by (normalizedUrl, postedDate truncated-to-second)
            var existingMap = new Dictionary<(string Url, DateTime Posted), int>();

            foreach (var e in existing)
            {
                if (string.IsNullOrWhiteSpace(e.PostUrl) || !e.PostedDate.HasValue)
                    continue;

                var key = (NormalizeUrl(e.PostUrl), TruncateToSecond(e.PostedDate.Value));
                if (!existingMap.ContainsKey(key))
                    existingMap[key] = e.Id;
            }

            foreach (var r in cleaned)
            {
                var key = (NormalizeUrl(r.PostUrl!), TruncateToSecond(r.PostedDate!.Value));

                if (!existingMap.TryGetValue(key, out var existingId))
                {
                    db.RawContents.Add(r);
                    continue;
                }

                // Update only if content changed (attach minimal entity)
                // We could load entity from change tracker if already tracked.
                var tracked = db.ChangeTracker.Entries<RawContent>()
                    .FirstOrDefault(x => x.Entity.Id == existingId)?.Entity;

                if (tracked != null)
                {
                    if (tracked.Content != r.Content)
                        tracked.Content = r.Content;
                }
                else
                {
                    // Attach stub, update content
                    var stub = new RawContent { Id = existingId };
                    db.RawContents.Attach(stub);

                    // We need existing content to compare; easiest is to just set (cheap)
                    stub.Content = r.Content;
                    db.Entry(stub).Property(x => x.Content).IsModified = true;
                }
            }
        }

        static string NormalizeUrl(string url) => url.Trim().ToUpperInvariant();

        private static DateTime TruncateToSecond(DateTime dt)
        {
            var ticks = dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond);
            return new DateTime(ticks, dt.Kind);
        }

        private static bool IsSnapshotResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return root.ValueKind == JsonValueKind.Object &&
                       root.TryGetProperty("snapshot_id", out _) &&
                       root.GetRawText().Trim().Length < 100;
            }
            catch
            {
                return false;
            }
        }

        private static string? ExtractSnapshotId(string jsonResponse)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                if (doc.RootElement.TryGetProperty("snapshot_id", out var snapshotIdElement))
                    return snapshotIdElement.GetString();
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }

}
