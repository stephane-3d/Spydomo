using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure
{
    public class RelevanceEvaluator : IRelevanceEvaluator
    {
        private readonly DbDataService _dbDataService;
        private readonly IGptRelevanceEvaluator _gptRelevanceEvaluator;

        public RelevanceEvaluator(DbDataService dbDataService, IGptRelevanceEvaluator gptRelevanceEvaluator)
        {
            _dbDataService = dbDataService;
            _gptRelevanceEvaluator = gptRelevanceEvaluator;
        }

        public async Task<bool> IsContentRelevantAsync(int companyId, string content, DataSourceTypeEnum sourceType)
        {
            var (companyName, keywords) = await _dbDataService.GetCompanyContextAsync(companyId);
            var lowerCompany = companyName.ToLowerInvariant();
            var lowerKeywords = keywords.Select(k => k.ToLowerInvariant()).ToList();
            var lowerContent = content.ToLowerInvariant();

            int score = 0;

            // Source-based trust bonus
            if (sourceType == DataSourceTypeEnum.FacebookReviews ||
                sourceType == DataSourceTypeEnum.G2 ||
                sourceType == DataSourceTypeEnum.Capterra ||
                sourceType == DataSourceTypeEnum.GetApp ||
                sourceType == DataSourceTypeEnum.TrustRadius ||
                sourceType == DataSourceTypeEnum.GartnerPeerInsights)
            {
                score += 3;
            }

            // Company name match
            if (lowerContent.Contains(lowerCompany))
                score += 2;

            // Exact keyword matches
            score += lowerKeywords.Count(k => lowerContent.Contains(k));

            // Fuzzy keyword matches
            score += lowerKeywords.Count(k => IsFuzzyMatch(k, lowerContent));

            if (score >= 3)
                return true;

            if (score <= 1)
                return false;

            // Borderline case — call GPT
            return await _gptRelevanceEvaluator.EvaluateRelevanceAsync(companyId, companyName, content, keywords);
        }

        private static bool IsFuzzyMatch(string keyword, string text)
        {
            // Normalize
            keyword = keyword.ToLowerInvariant().Trim();
            text = text.ToLowerInvariant();

            // Simple partial presence
            if (text.Contains(keyword))
                return false; // already counted as exact match

            // Try partial word matches
            var keywordParts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return keywordParts.Any(part =>
                part.Length >= 4 && text.Contains(part));
        }
    }
}
