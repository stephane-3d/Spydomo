using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spydomo.Infrastructure.PulseRules
{
    // Normalized result your rules can consume
    public sealed record ReviewFields(
        string? Title,
        string? Overall,
        string? Pros,
        string? Cons,
        string CanonicalText // concatenation suitable for classification
    );

    public static class ReviewContentReader
    {
        /// <summary>
        /// Normalize vendor-specific review JSON (Capterra/G2) into structured fields + canonical text.
        /// </summary>
        public static ReviewFields Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new ReviewFields(null, null, null, null, "");

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("Text", out var textEl))
                    return new ReviewFields(null, null, null, null, "");

                if (textEl.ValueKind == JsonValueKind.Object)
                {
                    // Capterra schema
                    string? title = GetString(textEl, "title");
                    string? overall = GetString(textEl, "overall");
                    string? pros = GetString(textEl, "pros");
                    string? cons = GetString(textEl, "cons");

                    var canonical = Concat(title, overall, pros, cons);
                    return new ReviewFields(title, overall, pros, cons, canonical);
                }
                if (textEl.ValueKind == JsonValueKind.String)
                {
                    // G2 schema: a big Q&A blob
                    var raw = textEl.GetString() ?? "";
                    var (title, overall, pros, cons) = ParseG2Blob(raw);
                    var canonical = Concat(title, overall, pros, cons, raw);
                    return new ReviewFields(title, overall, pros, cons, canonical);
                }

                // Fallback
                return new ReviewFields(null, null, null, null, textEl.GetRawText());
            }
            catch
            {
                // Not valid JSON; best we can do is return original string as canonical
                return new ReviewFields(null, null, null, null, json);
            }
        }

        private static string Concat(params string?[] parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(NormalizeWhitespace(p));
            }
            return sb.ToString();
        }

        private static string? GetString(JsonElement obj, string prop)
            => obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
                ? NullIfEmpty(el.GetString())
                : null;

        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s;

        private static string NormalizeWhitespace(string s)
            => Regex.Replace(s, @"\s+", " ").Trim();

        /// <summary>
        /// Parse G2 Q&A blob like:
        ///  "Question: What do you like best ... - Answer: ...\n\nQuestion: What do you dislike ... - Answer: ...\n\nQuestion: What problems ... - Answer: ..."
        /// Extract Pros/Cons/Overall heuristically (English-focused; safe fallback to raw).
        /// </summary>
        private static (string? Title, string? Overall, string? Pros, string? Cons) ParseG2Blob(string raw)
        {
            string? title = null;
            string? overall = null;
            string? pros = null;
            string? cons = null;

            // Split by "Question:" boundaries and capture following "Answer:"
            // We’ll build a dictionary question -> answer
            var pairs = new List<(string q, string a)>();
            var blocks = Regex.Split(raw, @"(?=Question:\s*)", RegexOptions.IgnoreCase);
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;
                var qMatch = Regex.Match(block, @"Question:\s*(.+?)(?:\r?\n| - Answer:|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var aMatch = Regex.Match(block, @"Answer:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (qMatch.Success && aMatch.Success)
                {
                    var q = NormalizeWhitespace(qMatch.Groups[1].Value);
                    var a = NormalizeWhitespace(aMatch.Groups[1].Value);
                    pairs.Add((q, a));
                }
            }

            // Heuristics: map common G2 questions to fields
            foreach (var (q, a) in pairs)
            {
                var qLower = q.ToLowerInvariant();

                if (qLower.Contains("like best") || qLower.Contains("what do you like") || qLower.Contains("pros"))
                    pros = a;

                else if (qLower.Contains("dislike") || qLower.Contains("what do you dislike") || qLower.Contains("cons"))
                    cons = a;

                else if (qLower.Contains("problems") || qLower.Contains("benefit") || qLower.Contains("how is that benefiting"))
                    overall = a;

                // keep first non-empty as title if we have something that looks short
                if (title is null && a.Length <= 120)
                    title = a;
            }

            // Fallbacks if nothing matched
            if (pros is null || cons is null || overall is null)
            {
                // If no structured fields identified, try simple bullet-like split
                var lines = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(NormalizeWhitespace)
                               .Where(l => l.Length > 0)
                               .ToList();

                if (pros is null)
                    pros = lines.FirstOrDefault(l => l.StartsWith("- ") || l.StartsWith("• ")) ?? null;
                if (cons is null && lines.Count > 2)
                    cons = lines.Skip(1).FirstOrDefault();
                if (overall is null)
                    overall = lines.Count > 0 ? lines.Last() : null;

                if (title is null)
                    title = lines.FirstOrDefault(l => l.Length <= 120);
            }

            return (title, overall, pros, cons);
        }
    }
}
