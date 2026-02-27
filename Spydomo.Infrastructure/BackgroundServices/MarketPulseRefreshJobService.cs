using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class MarketPulseRefreshJobService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IMarketPulseService _marketPulseService;
        private readonly ILogger<MarketPulseRefreshJobService> _logger;

        // safety caps (optional)
        private const int MaxGroupsPerRun = 2000;

        public MarketPulseRefreshJobService(
            IDbContextFactory<SpydomoContext> dbFactory,
            IMarketPulseService marketPulseService,
            ILogger<MarketPulseRefreshJobService> logger)
        {
            _dbFactory = dbFactory;
            _marketPulseService = marketPulseService;
            _logger = logger;
        }

        public async Task RunAsync(IJobCancellationToken hangfireToken)
        {
            _logger.LogInformation("⏱ MarketPulseRefreshJobService > RunAsync started");

            var ct = hangfireToken?.ShutdownToken ?? CancellationToken.None;

            try
            {
                List<string> publicSlugs;

                await using (var db = await _dbFactory.CreateDbContextAsync(ct))
                {
                    publicSlugs = await db.CompanyGroups
                        .AsNoTracking()
                        .Where(g => !g.IsPrivate && g.Slug != null)
                        .OrderBy(g => g.Id)
                        .Select(g => g.Slug!)
                        .Take(MaxGroupsPerRun)
                        .ToListAsync(ct);
                }

                if (publicSlugs.Count == 0)
                {
                    _logger.LogInformation("No public groups found for pulse refresh.");
                    return;
                }

                foreach (var slug in publicSlugs)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(slug))
                        continue;

                    try
                    {
                        _logger.LogInformation($"Processing market pulse refresh for {slug}.");
                        await _marketPulseService.GetPulseAsync(slug, forceRefresh: true, ct: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error refreshing market pulse for {Slug}", slug);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Market pulse refresh job cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running MarketPulseRefreshJobService");
                throw;
            }
            _logger.LogInformation("✅ MarketPulseRefreshJobService > RunAsync completed");
        }
    }
}
