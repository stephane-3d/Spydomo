using System.Text;

namespace Spydomo.Web.Classes
{
    public static class UiText
    {
        public static string FormatPercent(decimal v) => $"{Math.Round(v * 100m):0}%";

        public static string InfoTip(string? reason, decimal confidence)
        {
            var sb = new StringBuilder();
            sb.Append("Confidence: ").Append(FormatPercent(confidence));
            if (!string.IsNullOrWhiteSpace(reason)) sb.Append('\n').Append(reason);
            return sb.ToString();
        }

        public static IEnumerable<string> FlattenEvidence(List<List<string>>? groups, int maxLines)
        {
            if (groups is null) yield break;
            int n = 0;
            foreach (var g in groups)
                foreach (var s in g)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    yield return s;
                    if (++n >= maxLines) yield break;
                }
        }

        public static string HtmlDecode(string? s)
                => string.IsNullOrEmpty(s) ? "" : System.Net.WebUtility.HtmlDecode(s);

    }
}
