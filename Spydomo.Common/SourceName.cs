namespace Spydomo.Common
{
    public static class SourceName
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["blog"] = "Blog",
            ["blog / articles"] = "Blog",
            ["articles"] = "Blog",
            ["news"] = "News",
            ["news / press"] = "News",
            ["linkedin"] = "LinkedIn",
            ["facebook"] = "Facebook",
            ["instagram"] = "Instagram",
            ["x"] = "X",
            ["twitter"] = "X",
            ["youtube"] = "YouTube",
            ["tiktok"] = "TikTok",
            ["reddit"] = "Reddit",
            ["g2"] = "G2",
            ["capterra"] = "Capterra",
            ["trustradius"] = "TrustRadius",
            ["getapp"] = "GetApp",
            ["software advice"] = "SoftwareAdvice",
            ["producthunt"] = "ProductHunt",
            ["github"] = "GitHub"
        };

        public static string Normalize(string? raw)
            => string.IsNullOrWhiteSpace(raw) ? "Unknown"
             : Map.TryGetValue(raw.Trim(), out var v) ? v
             : raw.Trim(); // keep as-is but normalized once
    }

}
