using System.Text.RegularExpressions;

namespace Spydomo.Infrastructure
{
    public static class KeywordExtractor
    {
        private static readonly HashSet<string> StopEn = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","and","or","but","for","nor","on","in","at","to","from","by","of","with","is","are","was","were","be","been","being","it","this","that"
        };

        public static List<string> ExtractKeywords(string text, string lang)
        {
            if (string.IsNullOrWhiteSpace(text)) return new();
            var t = text.ToLowerInvariant();
            var tokens = Regex.Matches(t, @"\b[^\W\d_]{3,}\b", RegexOptions.CultureInvariant)
                              .Select(m => m.Value)
                              .Where(w => !StopEn.Contains(w))
                              .Take(500)
                              .ToList();
            var bigrams = new List<string>();
            for (int i = 0; i + 1 < tokens.Count; i++)
                bigrams.Add(tokens[i] + " " + tokens[i + 1]);
            var scored = tokens.Concat(bigrams)
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(8)
                .ToList();
            return scored;
        }
    }
}
