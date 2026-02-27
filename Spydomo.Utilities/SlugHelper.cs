using System.Text.RegularExpressions;

namespace Spydomo.Utilities
{
    public static class SlugHelper
    {
        public static string GenerateSlug(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            input = input.ToLowerInvariant();
            input = Regex.Replace(input, @"[^a-z0-9\s-]", "");
            input = Regex.Replace(input, @"[\s-]+", "-").Trim('-');
            return input;
        }

        public static async Task<string> GenerateUniqueSlugAsync(
            string baseInput,
            Func<string, Task<bool>> slugExistsAsync)
        {
            var slug = GenerateSlug(baseInput);
            var uniqueSlug = slug;
            int suffix = 2;

            while (await slugExistsAsync(uniqueSlug))
            {
                uniqueSlug = $"{slug}-{suffix}";
                suffix++;
            }

            return uniqueSlug;
        }
    }

}
