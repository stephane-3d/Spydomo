using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Globalization;
using System.Text.Json;


namespace Spydomo.Infrastructure.Parsers
{
    public class FacebookReviewParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.FacebookReviews;

        private readonly IRelevanceEvaluator _relevanceEvaluator;

        public FacebookReviewParser(IRelevanceEvaluator relevanceEvaluator)
        {
            _relevanceEvaluator = relevanceEvaluator;
        }

        public async Task<List<RawContent>> Parse(
            string jsonResponse,
            int companyId,
            DataSource source,
            DateTime? lastUpdate,
            OriginTypeEnum originType = OriginTypeEnum.UserGenerated)
        {
            var feedbackList = new List<RawContent>();

            if (string.IsNullOrWhiteSpace(jsonResponse))
                return feedbackList;

            try
            {
                var reviews = JsonDocument.Parse(jsonResponse).RootElement;

                if (reviews.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("Expected JSON array at root for Facebook reviews, but got something else.");
                    return feedbackList;
                }

                foreach (var review in reviews.EnumerateArray())
                {
                    try
                    {
                        string url = review.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";

                        DateTime postedDate;
                        if (!TryGetUtcDateTime(review, "review_time", out postedDate) &&
                            !TryGetUtcDateTime(review, "date", out postedDate))
                        {
                            postedDate = DateTime.UtcNow;
                        }

                        string reviewText = review.TryGetProperty("review_content", out var contentProp) ? contentProp.GetString() ?? "" : "";

                        bool recommends = review.TryGetProperty("recommends", out var recProp) && recProp.ValueKind == JsonValueKind.True;
                        int reactions = GetSafeInt(review, "number_of_reactions_to_review");
                        int totalReviews = GetSafeInt(review, "total_reviews");
                        int recommendedPercentage = GetSafeInt(review, "recommended_perc");

                        // review_reaction_types: array of strings
                        string reactionTypes = "";
                        if (review.TryGetProperty("review_reaction_types", out var reactionTypesProp) && reactionTypesProp.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var reaction in reactionTypesProp.EnumerateArray())
                            {
                                if (reaction.ValueKind == JsonValueKind.String)
                                    list.Add(reaction.GetString() ?? "");
                            }
                            reactionTypes = string.Join(", ", list);
                        }

                        var metadata = new Dictionary<string, object>
                        {
                            ["Recommends"] = recommends,
                            ["Reactions"] = reactions,
                            ["ReactionTypes"] = reactionTypes,
                            ["TotalReviews"] = totalReviews,
                            ["RecommendedPercentage"] = recommendedPercentage
                        };

                        if (recommendedPercentage > 0 && totalReviews > 0)
                        {
                            metadata["Message"] = $"Recommended by {recommendedPercentage}% of {totalReviews} reviewers";
                        }
                        else if (recommends)
                        {
                            metadata["Message"] = "This reviewer recommends the product";
                        }
                        else if (!recommends)
                        {
                            metadata["Message"] = "This reviewer does not recommend the product";
                        }

                        bool isRelevant = await _relevanceEvaluator.IsContentRelevantAsync(companyId, reviewText, DataSourceTypeEnum.FacebookReviews);

                        if (!isRelevant)
                            continue;

                        var enriched = new
                        {
                            Text = TextHelper.CleanAndNormalize(reviewText.Trim()),
                            Metadata = metadata
                        };

                        var engagementScore = reactions + (recommends ? 2 : 0);

                        feedbackList.Add(new RawContent
                        {
                            CompanyId = companyId,
                            PostUrl = url,
                            PostedDate = postedDate,
                            Content = JsonSerializer.Serialize(enriched),
                            DataSourceTypeId = (int)DataSourceTypeEnum.FacebookReviews,
                            Status = RawContentStatusEnum.NEW,
                            CreatedAt = DateTime.UtcNow,
                            RawResponse = review.GetRawText(),
                            OriginType = originType,
                            EngagementScore = engagementScore
                        });

                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"Skipping Facebook review due to parsing error: {innerEx.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Facebook reviews: {ex.Message}");
                throw;
            }

            return feedbackList;
        }


        public Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            return Task.FromResult<string?>(null);
        }

        private int GetSafeInt(JsonElement post, string propertyName)
        {
            return post.TryGetProperty(propertyName, out var prop) &&
                   prop.ValueKind == JsonValueKind.Number &&
                   prop.TryGetInt32(out var val)
                ? val
                : 0;
        }

        private static bool TryGetUtcDateTime(JsonElement el, string propName, out DateTime utc)
        {
            utc = default;

            if (!el.TryGetProperty(propName, out var p) || p.ValueKind != JsonValueKind.String)
                return false;

            var s = p.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Most tolerant: handles ISO8601, "2026-01-01 12:34:56", etc.
            if (DateTimeOffset.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dto))
            {
                utc = dto.UtcDateTime;
                return true;
            }

            // If BrightData ever returns epoch seconds/ms as string, handle it too:
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                // Heuristic: ms if it's big
                utc = n > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(n).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(n).UtcDateTime;
                return true;
            }

            return false;
        }
    }


}
