using System.Text.RegularExpressions;

namespace Spydomo.Utilities
{
    public static class TopicKeyHelper
    {
        public static string Slugify(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return "unknown";
            var t = topic.Trim().ToLowerInvariant();
            t = Regex.Replace(t, @"[^\p{L}\p{Nd}]+", "-");
            t = Regex.Replace(t, @"-+", "-").Trim('-');
            if (t.Length > 64) t = t[..64];
            return t;
        }
    }
}
