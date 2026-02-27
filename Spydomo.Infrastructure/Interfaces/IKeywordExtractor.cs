using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IKeywordExtractor
    {
        Task<KeywordAndCategoryResponse> ExtractKeywordsAndCategoryAsync(
            string companyName,
            string companyUrl,
            int limit = 12,
            int? companyId = null,
            CancellationToken ct = default);
    }

}
