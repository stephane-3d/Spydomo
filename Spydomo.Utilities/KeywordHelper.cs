namespace Spydomo.Utilities
{
    public static class KeywordHelper
    {
        public static List<string> GetTopDistinctiveKeywords(
            List<(string Keyword, double Confidence)> keywords,
            string companyName,
            int maxCount = 10)
        {
            return keywords
                .Where(k =>
                    !string.Equals(k.Keyword, companyName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(k => k.Confidence)
                .Take(maxCount)
                .Select(k => k.Keyword)
                .ToList();
        }
    }
}
