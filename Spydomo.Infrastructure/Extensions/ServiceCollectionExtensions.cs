using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spydomo.Infrastructure.AiServices;
using Spydomo.Infrastructure.BackgroundServices;
using Spydomo.Infrastructure.Billing;
using Spydomo.Infrastructure.Caching;
using Spydomo.Infrastructure.Clerk;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.Parsers;
using Spydomo.Infrastructure.PulseRules;
using Spydomo.Infrastructure.PulseRules.CompanyContent;

namespace Spydomo.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSpydomoShared(this IServiceCollection services)
        {
            // Pure shared services (no IJSRuntime, no HttpContext assumptions)
            services.AddScoped<DbDataService>();
            services.AddScoped<ICompanyService, CompanyService>();
            services.AddMemoryCache();

            services.AddScoped<CompanyDataService>();
            services.AddScoped<ISlugService, SlugService>();
            services.AddScoped<IBrightDataService, BrightDataService>();
            services.AddScoped<UserSyncService>();
            services.AddScoped<IFeedbackDataService, FeedbackDataService>();

            // Repos / services
            services.AddScoped<IStrategicSummaryRepository, StrategicSummaryRepository>();
            services.AddScoped<ISemanticSignalRepository, SemanticSignalRepository>();
            services.AddScoped<IPulseObservationRepository, PulseObservationRepository>();
            services.AddScoped<IPostingWindowStatsRepository, PostingWindowStatsRepository>();
            services.AddScoped<IEngagementStatsRepository, EngagementStatsRepository>();
            
            // misc clients/services
            services.AddTransient<GoogleSearchService>();
            services.AddHttpClient<PageContentFetcherService>();
            services.AddHttpClient<OpenAIService>();

            services.AddScoped<RawContentProcessor>();
            services.AddScoped<IAiSummarizer, AiSummarizer>();
            services.AddScoped<IFeedWriter, StrategicFeedWriter>();
            services.AddScoped<IRelevanceEvaluator, RelevanceEvaluator>();
            services.AddScoped<IContentAdapter, ContentAdapter>();
            services.AddScoped<IAiUsageLogger, AiUsageLogger>();
            services.AddScoped<StrategicSummaryService>();
            services.AddScoped<StrategicSignalCacheService>();
            services.AddScoped<ISnapshotTrackerService, SnapshotTrackerService>();
            services.AddScoped<IMarketPulseService, MarketPulseService>();
            services.AddScoped<IMarketPulseGenerator, MarketPulseGenerator>();

            services.AddScoped<ICompanyRelationsService, CompanyRelationsService>();
            services.AddScoped<ICompanyRelationsReconciliationService, CompanyRelationsReconciliationService>();
            services.AddScoped<ICompanySuggestionService, CompanySuggestionService>();

            // OpenAI
            services.AddHttpClient<OpenAiKeywordExtractor>();
            services.AddScoped<IKeywordExtractor, PerplexityKeywordExtractor>();
            services.AddScoped<IGptRelevanceEvaluator, OpenAiGptRelevanceEvaluator>();
            services.AddHttpClient<OpenAiEmbeddingService>();
            services.AddScoped<ICompanyContextExtractor, OpenAiCompanyContextExtractor>();
            services.AddScoped<IFeedItemExtractor, OpenAiFeedItemExtractor>();
            services.AddHttpClient<OpenAiPulseAgent>();
            services.AddScoped<IPulseAgent, OpenAiPulseAgent>();
            services.AddHttpClient<IPerplexityCompanyLandscapeClient, PerplexityCompanyLandscapeClient>();

            // Normalization
            services.AddScoped<IThemeNormalizer, ThemeNormalizer>();
            services.AddScoped<ITagNormalizer, TagNormalizer>();

            // Parsers
            services.AddScoped<IFeedbackParser, G2Parser>();
            services.AddScoped<IFeedbackParser, RedditParser>();
            services.AddScoped<IFeedbackParser, LinkedinParser>();
            services.AddScoped<IFeedbackParser, CapterraParser>();
            services.AddScoped<IFeedbackParser, BlogParser>();
            services.AddScoped<IFeedbackParser, NewsroomParser>();
            services.AddScoped<IFeedbackParser, InstagramParser>();
            services.AddScoped<IFeedbackParser, FacebookPostParser>();
            services.AddScoped<IFeedbackParser, FacebookReviewParser>();

            services.AddScoped<FeedbackParserFactory>();
            services.AddScoped<CompanyContentParser>();

            services.AddSingleton<CanonicalThemeEmbeddingCache>();
            services.AddSingleton<CanonicalTagEmbeddingCache>();
            services.AddSingleton<ISignalTypeOptionsProvider, SignalTypeOptionsProvider>();

            services.AddScoped<ISignalTypeResolver, SignalTypeResolver>();

            // IMPORTANT: assembly scanning (see section below)
            RegisterRules(services);

            return services;
        }

        public static IServiceCollection AddSpydomoWeb(this IServiceCollection services)
        {
            services.AddScoped<ClerkJsInterop>();
            services.AddScoped<IClientContextService, ClientContextService>();
            services.AddScoped<GroupState>();
            services.AddScoped<DatasheetService>();
            services.AddScoped<ISignalsLibraryService, SignalsLibraryService>();

            services.AddScoped<ICompanyGroupService, CompanyGroupService>();
            services.AddScoped<IDashboardService, DashboardService>();

            services.AddScoped<ISubscriptionService, StripeSubscriptionManager>();

            services.AddScoped<IFeedbackParserResolver, FeedbackParserResolver>();
            services.AddScoped<ICurrentUserState, CurrentUserState>();

            services.AddScoped<ISqlRunnerService, SqlRunnerService>();

            return services;
        }

        public static IServiceCollection AddSpydomoWorker(this IServiceCollection services)
        {

            services.AddScoped<IInternalContentService, InternalContentService>();
            services.AddScoped<ExternalFeedbackOrchestrator>();
            services.AddScoped<GistJobService>();
            services.AddScoped<StrategicSummaryJobService>();
            services.AddScoped<EmbeddingBackfillJobService>();
            services.AddScoped<MarketPulseRefreshJobService>();
            services.AddScoped<AccountCleanupService>();
            
            services.AddScoped<ICompanyContentRule, CompanyObservationRule>();
            services.AddScoped<ICompanyContentRule, ContentEngagementSpikeRule>();
            services.AddScoped<ICompanyContentRule, PostingFrequencySpikeRule>();
            
            services.AddScoped<ICompanyGroupStrategicSummaryStateStore, CompanyGroupStrategicSummaryStateStore>();
            services.AddScoped<WarmupService>();


            return services;
        }

        public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration cfg)
        {
            // ACS EmailClient
            var conn = cfg["AcsEmail:ConnectionString"]
                ?? throw new InvalidOperationException("Missing config: AcsEmail:ConnectionString");

            services.AddSingleton(_ => new EmailClient(conn));

            // Slack notifier: usually uses HttpClient
            services.AddHttpClient<ISlackNotifier, SlackNotifier>();

            // Your EmailService depends on EmailClient + IConfiguration + ILogger
            services.AddScoped<IEmailService, EmailService>();

            return services;
        }


        private static void RegisterRules(IServiceCollection services)
        {
            var contractsAsm = typeof(ITrackProcessor).Assembly;
            var infraAsm = typeof(ServiceCollectionExtensions).Assembly;

            var asms = new[] { contractsAsm, infraAsm }
                .Distinct()
                .ToArray();

            services.AddByInterfaceScan(
                lifetime: ServiceLifetime.Scoped,
                assemblies: asms,
                serviceInterfaceRoots: new[] { typeof(ITrackProcessor) }
            );
        }
    }

}
