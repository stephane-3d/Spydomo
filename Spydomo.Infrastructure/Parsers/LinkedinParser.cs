using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Globalization;
using System.Text.Json;

namespace Spydomo.Infrastructure.Parsers
{
    public class LinkedinParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.Linkedin;

        private readonly IRelevanceEvaluator _relevanceEvaluator;
        public LinkedinParser(IRelevanceEvaluator relevanceEvaluator)
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
                var posts = JsonDocument.Parse(jsonResponse).RootElement;

                if (posts.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("Expected JSON array at root for LinkedIn, but got something else.");
                    return feedbackList;
                }

                foreach (var post in posts.EnumerateArray())
                {
                    try
                    {
                        if (!post.TryGetProperty("url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String)
                            continue;

                        if (!post.TryGetProperty("date_posted", out var dateProp) || dateProp.ValueKind != JsonValueKind.String)
                            continue;

                        string url = urlProp.GetString();
                        var dateStr = dateProp.GetString();
                        if (string.IsNullOrWhiteSpace(dateStr))
                            continue;

                        if (!DateTime.TryParse(
                                dateStr,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var postedDate))
                        {
                            continue;
                        }

                        string title = post.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                        string text = post.TryGetProperty("post_text", out var textProp) ? textProp.GetString() ?? "" : "";

                        int numLikes = post.TryGetProperty("num_likes", out var likesProp) && likesProp.TryGetInt32(out var likesVal) ? likesVal : 0;
                        int numComments = post.TryGetProperty("num_comments", out var commentsCountProp) && commentsCountProp.TryGetInt32(out var commentsVal) ? commentsVal : 0;

                        int userFollowers = GetSafeInt(post, "user_followers");
                        int userPosts = GetSafeInt(post, "user_posts");
                        int userArticles = GetSafeInt(post, "user_articles");
                        int videoDuration = GetSafeInt(post, "video_duration");
                        int numConnections = GetSafeInt(post, "num_connections");

                        string postType = post.TryGetProperty("post_type", out var postTypeProp) ? postTypeProp.GetString() ?? "" : "";
                        string accountType = post.TryGetProperty("account_type", out var accountTypeProp) ? accountTypeProp.GetString() ?? "" : "";

                        if (postType == "repost" && post.TryGetProperty("repost", out var repostProp))
                        {
                            if (repostProp.TryGetProperty("repost_text", out var repostTextProp))
                            {
                                var repostText = repostTextProp.GetString();
                                if (!string.IsNullOrWhiteSpace(repostText))
                                {
                                    text = $"{text}\n\n---\n\n[Repost Content]\n\n{repostText}".Trim();
                                }
                            }
                        }

                        var metadata = new Dictionary<string, object>
                        {
                            ["Likes"] = numLikes,
                            ["Comments"] = numComments,
                            ["UserFollowers"] = userFollowers,
                            ["UserPosts"] = userPosts,
                            ["UserArticles"] = userArticles,
                            ["PostType"] = postType,
                            ["AccountType"] = accountType,
                            ["VideoDuration"] = videoDuration,
                            ["Connections"] = numConnections
                        };

                        var fullContent = $"{title}\n\n{text}".Trim();

                        bool isRelevant = originType == OriginTypeEnum.CompanyGenerated ||
                            await _relevanceEvaluator.IsContentRelevantAsync(companyId, fullContent, DataSourceTypeEnum.Linkedin);

                        if (!isRelevant)
                            continue;

                        var enriched = new
                        {
                            Title = TextHelper.CleanAndNormalize(title),
                            Text = TextHelper.CleanAndNormalize(text),
                            Metadata = metadata
                        };

                        var engagementScore = numLikes + numComments * 2;

                        feedbackList.Add(new RawContent
                        {
                            CompanyId = companyId,
                            PostUrl = url,
                            PostedDate = postedDate,
                            Content = JsonSerializer.Serialize(enriched),
                            DataSourceTypeId = (int)DataSourceTypeEnum.Linkedin,
                            Status = RawContentStatusEnum.NEW,
                            CreatedAt = DateTime.UtcNow,
                            RawResponse = post.GetRawText(),
                            OriginType = originType,
                            EngagementScore = engagementScore
                        });
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"Skipping LinkedIn post due to parsing error: {innerEx.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LinkedIn data: {ex.Message}");
                throw;
            }

            return feedbackList;
        }


        private int GetSafeInt(JsonElement post, string fieldName)
        {
            return post.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val)
                ? val
                : 0;
        }


        public Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            return null;
        }
    }
}
