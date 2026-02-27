using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class ExternalFeedbackOrchestrator
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IFeedbackDataService _feedback;
        private readonly ILogger<ExternalFeedbackOrchestrator> _logger;

        public ExternalFeedbackOrchestrator(
            IDbContextFactory<SpydomoContext> dbFactory,
            IFeedbackDataService feedback,
            ILogger<ExternalFeedbackOrchestrator> logger)
        {
            _dbFactory = dbFactory;
            _feedback = feedback;
            _logger = logger;
        }
        public Task RunForAllCompaniesAsync()
            => RunForAllCompaniesAsync(force: false, ct: CancellationToken.None);

        public Task RunForAllCompaniesForceAsync()
            => RunForAllCompaniesAsync(force: true, ct: CancellationToken.None);


        [DisableConcurrentExecution(timeoutInSeconds: 60 * 10)]
        public async Task RunForAllCompaniesAsync(bool force = false, CancellationToken ct = default)
        {
            _logger.LogInformation("⏱ External Feedback orchestrator > RunForAllCompaniesAsync started");
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var reviewsCutoff = DateTime.UtcNow.AddDays(-15);

            const int batchSize = 50;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // ✅ Select companies that need ANY external fetch (reviews OR reddit OR linkedin OR fb reviews)
            var companiesToUpdate = await db.Companies
                .AsNoTracking()
                .Where(c =>
                    // Review sources due?
                    c.DataSources.Any(ds =>
                        (ds.TypeId == (int)DataSourceTypeEnum.G2 || ds.TypeId == (int)DataSourceTypeEnum.Capterra) &&
                        (force || ds.LastUpdate == null || ds.LastUpdate < reviewsCutoff))

                    // Reddit due?
                    || (force || c.LastRedditLookup == null || c.LastRedditLookup < cutoff)

                    // LinkedIn due?
                    || (force || c.LastLinkedinLookup == null || c.LastLinkedinLookup < cutoff)

                    // Facebook reviews due? (and not explicitly disabled)
                    || ((force || c.LastFacebookReviewsLookup == null || c.LastFacebookReviewsLookup < cutoff)
                        && c.HasFacebookReviews != false)
                )
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            foreach (var companyId in companiesToUpdate)
            {
                ct.ThrowIfCancellationRequested();
                await RunForCompanyAsync(companyId, force, ct);
                await Task.Delay(500, ct);
            }
            _logger.LogInformation("✅ External Feedback orchestrator > RunForAllCompaniesAsync completed");
        }

        // ✅ Hangfire-friendly per-company entrypoints (NO optional params)
        public Task RunForCompanyAsync(int companyId)
            => RunForCompanyAsync(companyId, force: false, CancellationToken.None);

        public Task RunForCompanyForceAsync(int companyId)
            => RunForCompanyAsync(companyId, force: true, CancellationToken.None);

        public async Task RunForCompanyAsync(int companyId, bool force = false, CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);

            _logger.LogInformation("External fetch starting for companyId={CompanyId} force={Force}", companyId, force);

            // Reviews: let the parser-based orchestrator decide based on DataSources.LastUpdate (or ignore if force is true)
            // If you want "force", simplest is: ignore force here and add "force" handling inside FetchReviewsForCompany.
            // For now: just call it; it already checks LastUpdate and sets it.
            await _feedback.FetchReviewsForCompany(companyId);

            if (force)
            {
                await _feedback.FetchRedditMentionsForCompany(companyId);
                await _feedback.FetchLinkedInMentionsForCompany(companyId);
                await _feedback.FetchFacebookReviewsAsync(companyId);
            }
            else
            {
                // Use the company-level timestamps to avoid redundant calls
                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var meta = await db.Companies.AsNoTracking()
                    .Where(x => x.Id == companyId)
                    .Select(x => new
                    {
                        x.LastRedditLookup,
                        x.LastLinkedinLookup,
                        x.LastFacebookReviewsLookup,
                        x.HasFacebookReviews
                    })
                    .FirstOrDefaultAsync(ct);

                if (meta == null) return;

                if (meta.LastRedditLookup == null || meta.LastRedditLookup < cutoff)
                    await _feedback.FetchRedditMentionsForCompany(companyId);

                if (meta.LastLinkedinLookup == null || meta.LastLinkedinLookup < cutoff)
                    await _feedback.FetchLinkedInMentionsForCompany(companyId);

                if ((meta.LastFacebookReviewsLookup == null || meta.LastFacebookReviewsLookup < cutoff)
                    && meta.HasFacebookReviews != false)
                    await _feedback.FetchFacebookReviewsAsync(companyId);
            }

            _logger.LogInformation("External fetch completed for companyId={CompanyId}", companyId);
        }
    }

}
