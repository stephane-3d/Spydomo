using System.Text;

namespace Spydomo.Utilities
{
    public static class TextHelper
    {
        public static string CleanAndNormalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // 1. Unescape \uXXXX (e.g., \u0022 → ")
            var unescaped = System.Text.RegularExpressions.Regex.Unescape(input);

            // 2. Remove weird styled mathematical characters (U+1D400–U+1D7FF)
            unescaped = new string(unescaped
                .Where(c => c < 0x1D400 || c > 0x1D7FF)
                .ToArray());

            // 3. Optionally remove emojis and symbols (U+1F300–U+1FAFF)
            /*unescaped = new string(unescaped
                .Where(c => !char.IsSurrogate(c) && (c < 0x1F300 || c > 0x1FAFF))
                .ToArray());*/

            // 4. Normalize whitespace:
            //    - collapse multiple spaces
            //    - replace multiple newlines with a single one
            //    - trim
            string normalized = System.Text.RegularExpressions.Regex.Replace(unescaped, @"[ ]{2,}", " ");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"(\r?\n){2,}", "\n");
            normalized = normalized.Trim();

            return normalized;
        }

        public static string AddSpacesToCategoryName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(input[0]);

            for (int i = 1; i < input.Length; i++)
            {
                char current = input[i];
                char previous = input[i - 1];
                char? next = i + 1 < input.Length ? input[i + 1] : (char?)null;

                bool isCurrentUpper = char.IsUpper(current);
                bool isPrevUpper = char.IsUpper(previous);
                bool isCurrentDigit = char.IsDigit(current);
                bool isPrevDigit = char.IsDigit(previous);

                // Case 1: lower -> UPPER  (MarketingAnalytics)
                bool insertBeforeUpperFromLower =
                    isCurrentUpper && char.IsLower(previous);

                // Case 2: acronym boundary: UPPER + UPPER + lower  (APIDashboard)
                bool insertBeforeUpperFromAcronym =
                    isCurrentUpper && isPrevUpper && next.HasValue && char.IsLower(next.Value);

                // Case 3: digit boundaries: ...a2B, ...2B, ...A2
                bool digitBoundary = (isCurrentDigit && !isPrevDigit) ||
                                     (!isCurrentDigit && isPrevDigit);

                if (insertBeforeUpperFromLower || insertBeforeUpperFromAcronym || digitBoundary)
                {
                    sb.Append(' ');
                }

                sb.Append(current);
            }

            return sb.ToString().Trim();
        }

    }

}
