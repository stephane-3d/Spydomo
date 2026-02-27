using Hangfire;
using Spydomo.Infrastructure.BackgroundServices;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Worker
{
    public class RecurringJobsScheduler : IHostedService
    {
        private readonly IRecurringJobManager _recurringJobManager;

        public RecurringJobsScheduler(IRecurringJobManager recurringJobManager)
        {
            _recurringJobManager = recurringJobManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Keep options for non-queue settings (timezone, misfire handling, etc.)
            var utc = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };

            // Company data extraction: hourly safety net (queue: company-data)
            _recurringJobManager.AddOrUpdate<CompanyDataService>(
                recurringJobId: "company-data-extraction",
                methodCall: svc => svc.ExtractDataAsync(),
                cronExpression: "7 * * * *",
                options: utc,
                queue: "company-data");

            // Gist processing: every minute (queue: pipeline)
            _recurringJobManager.AddOrUpdate<GistJobService>(
                recurringJobId: "gist-processing",
                methodCall: s => s.RunAsync(JobCancellationToken.Null),
                cronExpression: "*/5 * * * *",
                options: utc,
                queue: "pipeline");

            // Strategic summary: every 10 minutes, offset (queue: pipeline)
            _recurringJobManager.AddOrUpdate<StrategicSummaryJobService>(
                recurringJobId: "strategic-summary",
                methodCall: s => s.RunAsync(JobCancellationToken.Null),
                cronExpression: "3-59/10 * * * *",
                options: utc,
                queue: "pipeline");

            // Hourly jobs (queue: hourly)
            _recurringJobManager.AddOrUpdate<ExternalFeedbackOrchestrator>(
                recurringJobId: "external-fetch-hourly",
                methodCall: x => x.RunForAllCompaniesAsync(),
                cronExpression: "8 * * * *",
                options: utc,
                queue: "hourly");

            _recurringJobManager.AddOrUpdate<IInternalContentService>(
                recurringJobId: "content-fetch-hourly",
                methodCall: x => x.FetchContentForAllCompanies(),
                cronExpression: "15 * * * *",
                options: utc,
                queue: "hourly");

            // Maintenance (queue: maintenance)
            _recurringJobManager.AddOrUpdate<MarketPulseRefreshJobService>(
                recurringJobId: "market-pulse-refresh",
                methodCall: s => s.RunAsync(JobCancellationToken.Null),
                cronExpression: "15 3 * * *",
                options: utc,
                queue: "maintenance");

            _recurringJobManager.AddOrUpdate<AccountCleanupService>(
                recurringJobId: "account-cleanup",
                methodCall: s => s.RunAsync(JobCancellationToken.Null),
                cronExpression: "25 * * * *",
                options: utc,
                queue: "maintenance");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
