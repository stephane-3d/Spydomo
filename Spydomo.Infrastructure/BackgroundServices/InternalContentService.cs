using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class InternalContentService : IInternalContentService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<InternalContentService> _logger;
        private readonly IBrightDataService _brightDataService;
        private readonly ISnapshotTrackerService _snapshotTracker;
        private readonly FeedbackParserFactory _parserFactory;

        public InternalContentService(
            IDbContextFactory<SpydomoContext> dbFactory,
            ILogger<InternalContentService> logger,
            IBrightDataService brightDataService,
            ISnapshotTrackerService snapshotTracker,
            FeedbackParserFactory parserFactory)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _brightDataService = brightDataService;
            _snapshotTracker = snapshotTracker;
            _parserFactory = parserFactory;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 60 * 10)]
        public async Task FetchContentForAllCompanies()
        {
            _logger.LogInformation("⏱ InternalContentService > FetchContentForAllCompanies called");

            await using var db = await _dbFactory.CreateDbContextAsync();
            const int batchSize = 50;

            var threshold = DateTime.UtcNow.AddDays(-7);

            var internalTypeIds = new[]
            {
                (int)DataSourceTypeEnum.Blog,
                (int)DataSourceTypeEnum.Linkedin,
                (int)DataSourceTypeEnum.Instagram,
                (int)DataSourceTypeEnum.Facebook,
                // optional if you fetch these as “company-generated”
                (int)DataSourceTypeEnum.Youtube,
                (int)DataSourceTypeEnum.EmailNewsletters,
                (int)DataSourceTypeEnum.News
            };

            var companyIdsToUpdate = await db.Companies
                .Where(c => c.DataSources.Any(ds =>
                    ds.TypeId != null &&
                    internalTypeIds.Contains(ds.TypeId.Value) &&
                    (ds.LastUpdate == null || ds.LastUpdate < threshold)))
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .Take(batchSize)
                .ToListAsync();

            if (!companyIdsToUpdate.Any())
            {
                _logger.LogInformation("🟢 All companies are up to date. No content fetch needed.");
                return;
            }
            else
            {
                _logger.LogInformation("🔄 Processing {Count} companies to fetch their content...", companyIdsToUpdate.Count);
            }

            foreach (var companyId in companyIdsToUpdate)
            {
                await FetchInternalContentForCompanyAsync(companyId);
                await Task.Delay(500); // avoid rate limits
            }
            _logger.LogInformation("✅ InternalContentService > FetchContentForAllCompanies done");
        }


        public async Task FetchInternalContentForCompanyAsync(int companyId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var threshold = DateTime.UtcNow.AddDays(-7);

            var stale = await db.DataSources
                .Where(ds => ds.CompanyId == companyId && ds.TypeId != null)
                .Where(ds => ds.LastUpdate == null || ds.LastUpdate < threshold)
                .Select(ds => ds.TypeId!.Value)
                .ToListAsync();

            if (stale.Contains((int)DataSourceTypeEnum.Blog))
                await FetchCompanyContentAsync(companyId);

            if (stale.Contains((int)DataSourceTypeEnum.Linkedin))
                await FetchLinkedinContentAsync(companyId);

            if (stale.Contains((int)DataSourceTypeEnum.Instagram))
                await FetchInstagramContentAsync(companyId);

            if (stale.Contains((int)DataSourceTypeEnum.Facebook))
                await FetchFacebookPostsAsync(companyId);

        }

        public async Task FetchCompanyContentAsync(int companyId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var dataSources = await db.DataSources
                .Where(ds =>
                    ds.CompanyId == companyId &&
                    (ds.TypeId == (int)DataSourceTypeEnum.Blog || ds.TypeId == (int)DataSourceTypeEnum.News))
                .ToListAsync();

            if (!dataSources.Any())
            {
                _logger.LogWarning("⚠️ No blog/news sources found for company {CompanyId}", companyId);
                return;
            }

            foreach (var source in dataSources)
            {
                var parser = _parserFactory.GetParser((DataSourceTypeEnum)source.TypeId);
                if (parser == null)
                {
                    _logger.LogWarning("❌ No parser found for source type {TypeId}", source.TypeId);
                    continue;
                }

                _logger.LogInformation("📥 Fetching content from {Url}", source.Url);

                try
                {
                    var html = await _brightDataService.FetchHtmlAsync(source.Url);
                    var content = await parser.Parse(html, companyId, source, source.LastUpdate);

                    if (content.Any())
                    {
                        await SaveInternalContentAsync(companyId, content);

                        _logger.LogInformation("✅ Saved {Count} posts from {Url}", content.Count, source.Url);
                    }
                    else
                    {
                        _logger.LogInformation("🟡 No new content from {Url}", source.Url);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🚨 Failed to fetch or parse content from {Url}. companyId {CompanyId}", source.Url, companyId);
                }
                finally
                {
                    _logger.LogInformation("LastUpdate date updated for {Url}", source.Url);
                    source.LastUpdate = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
        }

        public async Task FetchLinkedinContentAsync(int companyId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();

                var threshold = DateTime.UtcNow.AddDays(-7);

                var linkedInUrls = await db.DataSources
                    .Where(ds => ds.CompanyId == companyId &&
                                 ds.TypeId == (int)DataSourceTypeEnum.Linkedin &&
                                 (ds.LastUpdate == null || ds.LastUpdate < threshold))
                    .Select(ds => ds.Url)
                    .Distinct()
                    .ToListAsync();

                if (!linkedInUrls.Any())
                {
                    _logger.LogInformation("[LinkedIn] No outdated LinkedIn URLs found for company {CompanyId}", companyId);
                    return;
                }

                const string linkedInDatasetId = "gd_lyy3tktm25m4avu764";

                var extraParams = new Dictionary<string, string>
                {
                    { "type", "discover_new" },
                    { "discover_by", "company_url" }
                };

                var payload = linkedInUrls
                    .Select(url => new Dictionary<string, string> { { "url", url } })
                    .ToList();

                _logger.LogInformation("[LinkedIn] Triggering BrightData scrape for {Count} URL(s) for company {CompanyId}",
                    linkedInUrls.Count, companyId);

                var response = await _brightDataService.TriggerUrlScrapingAsync(linkedInDatasetId, payload, extraParams);
                var snapshotId = ExtractSnapshotId(response);

                if (!string.IsNullOrEmpty(snapshotId))
                {
                    await _snapshotTracker.TrackAsync(
                        snapshotId,
                        companyId,
                        (int)DataSourceTypeEnum.Linkedin,
                        JsonSerializer.Serialize(linkedInUrls),
                        "N/A",
                        OriginTypeEnum.CompanyGenerated
                    );
                }

                // update LastUpdate for all LinkedIn sources for this company
                var sourcesToUpdate = await db.DataSources
                    .Where(ds => ds.CompanyId == companyId &&
                                 ds.TypeId == (int)DataSourceTypeEnum.Linkedin)
                    .ToListAsync();

                foreach (var s in sourcesToUpdate)
                    s.LastUpdate = DateTime.UtcNow;

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LinkedIn] FetchLinkedinContentAsync error for company {CompanyId}", companyId);
            }
        }

        public async Task FetchInstagramContentAsync(int companyId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();

                var threshold = DateTime.UtcNow.AddDays(-7);

                var instagramUrls = await db.DataSources
                    .Where(ds => ds.CompanyId == companyId &&
                                 ds.TypeId == (int)DataSourceTypeEnum.Instagram &&
                                 (ds.LastUpdate == null || ds.LastUpdate < threshold))
                    .Select(ds => ds.Url)
                    .Distinct()
                    .ToListAsync();

                if (!instagramUrls.Any())
                {
                    _logger.LogInformation("[Instagram] No outdated Instagram URLs found for company {CompanyId}", companyId);
                    return;
                }

                const string instagramDatasetId = "gd_lk5ns7kz21pck8jpis";
                var startDate = DateTime.UtcNow.AddDays(-60).ToString("MM-dd-yyyy");

                var payload = instagramUrls.Select(url => new Dictionary<string, string>
                {
                    { "url", url },
                    { "start_date", startDate }
                }).ToList();

                var extraParams = new Dictionary<string, string>
                {
                    { "type", "discover_new" },
                    { "discover_by", "url" }
                };

                _logger.LogInformation("[Instagram] Triggering BrightData scrape for {Count} URL(s) for company {CompanyId}",
                    instagramUrls.Count, companyId);

                var response = await _brightDataService.TriggerUrlScrapingAsync(instagramDatasetId, payload, extraParams);
                var snapshotId = ExtractSnapshotId(response);

                if (!string.IsNullOrEmpty(snapshotId))
                {
                    await _snapshotTracker.TrackAsync(
                        snapshotId,
                        companyId,
                        (int)DataSourceTypeEnum.Instagram,
                        JsonSerializer.Serialize(instagramUrls),
                        "N/A",
                        OriginTypeEnum.CompanyGenerated
                    );
                }

                var sourcesToUpdate = await db.DataSources
                    .Where(ds => ds.CompanyId == companyId &&
                                 ds.TypeId == (int)DataSourceTypeEnum.Instagram)
                    .ToListAsync();

                foreach (var s in sourcesToUpdate)
                    s.LastUpdate = DateTime.UtcNow;

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Instagram] FetchInstagramContentAsync error for company {CompanyId}", companyId);
            }
        }

        public async Task FetchFacebookPostsAsync(int companyId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();

                var threshold = DateTime.UtcNow.AddDays(-7);

                var facebookUrls = await db.DataSources
                    .Where(ds => ds.CompanyId == companyId &&
                                 ds.TypeId == (int)DataSourceTypeEnum.Facebook &&
                                 (ds.LastUpdate == null || ds.LastUpdate < threshold))
                    .Select(ds => ds.Url)
                    .Distinct()
                    .ToListAsync();

                if (!facebookUrls.Any())
                {
                    _logger.LogInformation("[Facebook] No outdated Facebook URLs found for company {CompanyId}", companyId);
                    return;
                }

                const string facebookPostsDatasetId = "gd_lkaxegm826bjpoo9m5";
                var startDate = DateTime.UtcNow.AddDays(-60).ToString("MM-dd-yyyy");

                var payload = facebookUrls.Select(url => new Dictionary<string, string>
                {
                    { "url", url },
                    { "start_date", startDate },
                    { "include_profile_data", "true" }
                }).ToList();

                _logger.LogInformation("[Facebook] Triggering BrightData scrape for {Count} URL(s) for company {CompanyId}",
                    facebookUrls.Count, companyId);

                var response = await _brightDataService.TriggerUrlScrapingAsync(facebookPostsDatasetId, payload);
                var snapshotId = ExtractSnapshotId(response);

                if (!string.IsNullOrEmpty(snapshotId))
                {
                    await _snapshotTracker.TrackAsync(
                        snapshotId,
                        companyId,
                        (int)DataSourceTypeEnum.Facebook,
                        JsonSerializer.Serialize(facebookUrls),
                        "N/A",
                        OriginTypeEnum.CompanyGenerated
                    );
                }

                var sourcesToUpdate = await db.DataSources
                    .Where(ds => ds.CompanyId == companyId &&
                                 ds.TypeId == (int)DataSourceTypeEnum.Facebook)
                    .ToListAsync();

                foreach (var s in sourcesToUpdate)
                    s.LastUpdate = DateTime.UtcNow;

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Facebook] FetchFacebookPostsAsync error for company {CompanyId}", companyId);
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
                // keep null
            }

            return null;
        }

        private async Task SaveInternalContentAsync(int companyId, List<RawContent> contentList)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var postUrls = contentList
                .Where(c => !string.IsNullOrWhiteSpace(c.PostUrl))
                .Select(c => c.PostUrl!)
                .Distinct()
                .ToList();

            if (postUrls.Count == 0)
                return;

            var existingUrls = (await db.RawContents
                .Where(r => r.CompanyId == companyId && postUrls.Contains(r.PostUrl!))
                .Select(r => r.PostUrl!)
                .ToListAsync())
                .ToHashSet();

            var newContent = contentList
                .Where(c => !string.IsNullOrWhiteSpace(c.PostUrl) && !existingUrls.Contains(c.PostUrl!))
                .ToList();

            if (newContent.Any())
            {
                db.RawContents.AddRange(newContent);
                await db.SaveChangesAsync();
            }
        }
    }


}
