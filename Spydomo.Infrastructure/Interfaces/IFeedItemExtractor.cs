namespace Spydomo.Infrastructure.Interfaces
{
    public interface IFeedItemExtractor
    {
        Task<(string Gist, string WhyItMatters)> GenerateFeedItemAsync(
            string summaryGist,
            List<string> themes,
            string companyName,
            string contextSummary,
            int? companyId = null);
    }
}
