using Spydomo.Common.Enums;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure
{
    public static class SignalScoreCalculator
    {
        public async static Task<int> FromRawContent(RawContent content)
        {
            var type = (DataSourceTypeEnum)content.DataSourceTypeId;

            if (string.IsNullOrWhiteSpace(content.Content))
                return 0;

            if (!IsValidJson(content.Content))
            {
                // Fallback for unstructured content like blogs, news, etc.
                return type switch
                {
                    DataSourceTypeEnum.CompanyContent => 2,
                    DataSourceTypeEnum.News => 2,
                    DataSourceTypeEnum.Blog => 2,
                    _ => 0
                };
            }

            try
            {
                using var doc = JsonDocument.Parse(content.Content);
                var root = doc.RootElement;

                switch (type)
                {
                    case DataSourceTypeEnum.Facebook:
                    case DataSourceTypeEnum.FacebookReviews:
                        return FacebookScore(root);

                    case DataSourceTypeEnum.Linkedin:
                        return LinkedInScore(root);

                    case DataSourceTypeEnum.G2:
                    case DataSourceTypeEnum.Capterra:
                        return ReviewScore(root); // Based on star rating

                    case DataSourceTypeEnum.Reddit:
                        return RedditScore(root); // If supported

                    case DataSourceTypeEnum.Instagram:
                        return InstagramScore(root); // If supported

                    case DataSourceTypeEnum.CompanyContent:
                    case DataSourceTypeEnum.News:
                    case DataSourceTypeEnum.Blog:
                        return 2; // Neutral score for relevance, no engagement data
                                  // 
                    default:
                        return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalScore] Failed for RawContentId={content.Id}: {ex.Message}");
                return 0;
            }
        }

        public static bool IsValidJson(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            input = input.Trim();

            // Basic structure check
            if (!(input.StartsWith("{") && input.EndsWith("}")) &&
                !(input.StartsWith("[") && input.EndsWith("]")))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(input);
                return true;
            }
            catch
            {
                return false;
            }
        }


        private static int FacebookScore(JsonElement root)
        {
            if (!root.TryGetProperty("Metadata", out var meta)) return 0;

            int likes = GetInt(meta, "Likes");
            int comments = GetInt(meta, "Comments");
            int shares = GetInt(meta, "Shares");
            int followers = GetInt(meta, "PageFollowers");
            bool sponsored = GetBool(meta, "IsSponsored");

            int score = (likes * 2) + (comments * 3) + (shares * 4);
            score += Math.Max((int)Math.Round(Math.Log10(followers + 1) * 5), 0);

            if (sponsored) score -= 5;

            return Math.Max(score, 0);
        }

        private static int LinkedInScore(JsonElement root)
        {
            if (!root.TryGetProperty("Metadata", out var meta)) return 0;

            int likes = GetInt(meta, "Likes");
            int comments = GetInt(meta, "Comments");
            int followers = GetInt(meta, "UserFollowers");

            int score = (likes * 2) + (comments * 3);
            score += (int)Math.Round(Math.Sqrt(followers) * 0.05);

            return Math.Max(score, 0);
        }

        private static int ReviewScore(JsonElement root)
        {
            // G2 / Capterra root-level star rating
            if (root.TryGetProperty("Stars", out var stars))
            {
                double value = stars.GetDouble();
                return (int)(value * 2); // 5 stars = 10, 3 stars = 6, etc.
            }

            return 0;
        }

        private static int RedditScore(JsonElement root)
        {
            if (!root.TryGetProperty("Metadata", out var meta))
                return 0;

            int score = meta.TryGetProperty("Score", out var s) ? s.GetInt32() : 0;
            int numComments = meta.TryGetProperty("NumComments", out var c) ? c.GetInt32() : 0;

            int signal = (score * 2) + (numComments * 3); // Weight comments a bit higher

            return Math.Max(signal, 0);
        }

        private static int InstagramScore(JsonElement root)
        {
            if (!root.TryGetProperty("Metadata", out var meta)) return 0;

            int likes = GetInt(meta, "Likes");
            int comments = GetInt(meta, "Comments");
            int followers = GetInt(meta, "Followers");
            int videoPlays = GetInt(meta, "VideoPlayCount");
            bool isPaid = GetBool(meta, "IsPaidPartnership");

            int score = (likes * 3) + (comments * 2) + (videoPlays / 10);

            score += (int)Math.Round(Math.Sqrt(followers) * 0.25);

            if (isPaid) score -= 5;

            return Math.Max(score, 0);
        }


        private static int GetInt(JsonElement obj, string propertyName)
        {
            return obj.TryGetProperty(propertyName, out var prop) ? prop.GetInt32() : 0;
        }

        private static bool GetBool(JsonElement obj, string propertyName)
        {
            return obj.TryGetProperty(propertyName, out var prop) && prop.GetBoolean();
        }
    }

}
