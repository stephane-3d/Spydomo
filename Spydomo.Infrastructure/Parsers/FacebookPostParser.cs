using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Globalization;
using System.Text.Json;

namespace Spydomo.Infrastructure.Parsers
{
    public class FacebookPostParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.Facebook;

        private readonly IRelevanceEvaluator _relevanceEvaluator;

        public FacebookPostParser(IRelevanceEvaluator relevanceEvaluator)
        {
            _relevanceEvaluator = relevanceEvaluator;
        }

        public async Task<List<RawContent>> Parse(
                string jsonResponse,
                int companyId,
                DataSource source,
                DateTime? lastUpdate,
                OriginTypeEnum originType = OriginTypeEnum.CompanyGenerated)
        {
            var feedbackList = new List<RawContent>();

            if (string.IsNullOrWhiteSpace(jsonResponse))
                return feedbackList;

            try
            {
                var posts = JsonDocument.Parse(jsonResponse).RootElement;

                if (posts.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("Expected JSON array at root for Facebook, but got something else.");
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


                        string content = post.TryGetProperty("content", out var msgProp) ? msgProp.GetString() ?? "" : "";

                        if (string.IsNullOrWhiteSpace(content))
                            continue;

                        int numLikes = GetSafeInt(post, "likes");
                        int numComments = GetSafeInt(post, "num_comments");
                        int numShares = GetSafeInt(post, "num_shares");
                        int pageLikes = GetSafeInt(post, "page_likes");
                        int pageFollowers = GetSafeInt(post, "page_followers");
                        int videoViews = GetSafeInt(post, "video_view_count");

                        string postType = post.TryGetProperty("post_type", out var postTypeProp) ? postTypeProp.GetString() ?? "" : "";
                        bool isSponsored = post.TryGetProperty("is_sponsored", out var sponsoredProp) && sponsoredProp.ValueKind == JsonValueKind.True;

                        var metadata = new Dictionary<string, object>
                        {
                            ["Likes"] = numLikes,
                            ["Comments"] = numComments,
                            ["Shares"] = numShares,
                            ["PageLikes"] = pageLikes,
                            ["PageFollowers"] = pageFollowers,
                            ["VideoViews"] = videoViews,
                            ["PostType"] = postType,
                            ["IsSponsored"] = isSponsored
                        };

                        bool isRelevant = true;

                        if (originType != OriginTypeEnum.CompanyGenerated)
                        {
                            isRelevant = await _relevanceEvaluator.IsContentRelevantAsync(companyId, content, DataSourceTypeEnum.Facebook);
                        }

                        if (!isRelevant)
                            continue;

                        var enriched = new
                        {
                            Text = TextHelper.CleanAndNormalize(content.Trim()),
                            Metadata = metadata
                        };

                        var engagementScore = numLikes + numComments * 3 + numShares * 2;

                        var feedback = new RawContent
                        {
                            CompanyId = companyId,
                            PostUrl = url,
                            PostedDate = postedDate,
                            Content = JsonSerializer.Serialize(enriched),
                            DataSourceTypeId = (int)DataSourceTypeEnum.Facebook,
                            Status = RawContentStatusEnum.NEW,
                            CreatedAt = DateTime.UtcNow,
                            RawResponse = post.GetRawText(),
                            OriginType = originType,
                            EngagementScore = engagementScore,
                        };

                        feedbackList.Add(feedback);
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"Skipping Facebook post due to parsing error: {innerEx.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Facebook post data: {ex.Message}");
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



    }


}
