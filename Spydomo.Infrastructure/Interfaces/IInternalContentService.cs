namespace Spydomo.Infrastructure.Interfaces
{
    public interface IInternalContentService
    {
        Task FetchContentForAllCompanies();

        Task FetchInternalContentForCompanyAsync(int companyId);
        Task FetchCompanyContentAsync(int companyId);

        Task FetchLinkedinContentAsync(int companyId);
        Task FetchInstagramContentAsync(int companyId);
        Task FetchFacebookPostsAsync(int companyId);
        // You can later add:
        // Task FetchYouTubeVideosForCompany(int companyId);
        // Task FetchInstagramPostsForCompany(int companyId);
    }
}
