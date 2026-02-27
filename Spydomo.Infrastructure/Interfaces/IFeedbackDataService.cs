using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IFeedbackDataService
    {
        /// <summary>
        /// Fetch reviews for a specific company.
        /// </summary>
        Task FetchReviewsForCompany(int companyId, CancellationToken ct = default);

        Task StoreReviewsAsync(int companyId, List<RawContent> reviews, CancellationToken ct = default);

        Task<int?> FindCompanyIdByUrlAsync(string url, CancellationToken ct = default);

        Task FetchRedditMentionsForCompany(int companyId, CancellationToken ct = default);

        Task FetchLinkedInMentionsForCompany(int companyId, CancellationToken ct = default);

        Task FetchFacebookReviewsAsync(int companyId, CancellationToken ct = default);
    }


}
