using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Globalization;
using System.Text.Json;

namespace Spydomo.Infrastructure.Parsers
{
    public class InstagramParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.Instagram;

        private readonly IRelevanceEvaluator _relevanceEvaluator;

        public InstagramParser(IRelevanceEvaluator relevanceEvaluator)
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
                    Console.WriteLine("Expected JSON array at root for Instagram, but got something else.");
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

                        string description = post.TryGetProperty("description", out var captionProp) ? captionProp.GetString() ?? "" : "";

                        int numLikes = post.TryGetProperty("likes", out var likesProp) && likesProp.TryGetInt32(out var likesVal) ? likesVal : 0;
                        int numComments = post.TryGetProperty("num_comments", out var commentsCountProp) && commentsCountProp.TryGetInt32(out var commentsVal) ? commentsVal : 0;

                        string contentType = post.TryGetProperty("content_type", out var contentTypeProp) ? contentTypeProp.GetString() ?? "" : "";
                        bool isPaid = post.TryGetProperty("is_paid_partnership", out var paidProp) && paidProp.ValueKind == JsonValueKind.True;
                        int followers = post.TryGetProperty("followers", out var followersProp) && followersProp.TryGetInt32(out var followersVal) ? followersVal : 0;
                        int postsCount = post.TryGetProperty("posts_count", out var postsProp) && postsProp.TryGetInt32(out var postsVal) ? postsVal : 0;
                        int videoPlays = post.TryGetProperty("video_play_count", out var playsProp) && playsProp.TryGetInt32(out var playsVal) ? playsVal : 0;

                        string durations = "";
                        if (post.TryGetProperty("videos_duration", out var videoDurationsProp) && videoDurationsProp.ValueKind == JsonValueKind.Array)
                        {
                            var durationList = new List<string>();
                            foreach (var duration in videoDurationsProp.EnumerateArray())
                            {
                                if (duration.ValueKind == JsonValueKind.Number)
                                {
                                    durationList.Add(duration.GetRawText());
                                }
                            }
                            durations = string.Join(",", durationList);
                        }

                        var metadata = new Dictionary<string, object>
                        {
                            ["Likes"] = numLikes,
                            ["Comments"] = numComments,
                            ["ContentType"] = contentType,
                            ["IsPaidPartnership"] = isPaid,
                            ["Followers"] = followers,
                            ["PostsCount"] = postsCount,
                            ["VideoPlayCount"] = videoPlays,
                            ["VideoDurations"] = durations
                        };

                        bool isRelevant = originType == OriginTypeEnum.CompanyGenerated ||
                            await _relevanceEvaluator.IsContentRelevantAsync(companyId, description, DataSourceTypeEnum.Instagram);

                        if (!isRelevant)
                            continue;

                        var enriched = new
                        {
                            Text = TextHelper.CleanAndNormalize(description.Trim()),
                            Metadata = metadata
                        };

                        var engagementScore = numLikes + numComments * 2;

                        feedbackList.Add(new RawContent
                        {
                            CompanyId = companyId,
                            PostUrl = url,
                            PostedDate = postedDate,
                            Content = JsonSerializer.Serialize(enriched),
                            DataSourceTypeId = (int)DataSourceTypeEnum.Instagram,
                            Status = RawContentStatusEnum.NEW,
                            CreatedAt = DateTime.UtcNow,
                            RawResponse = post.GetRawText(),
                            OriginType = originType,
                            EngagementScore = engagementScore
                        });
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"Skipping Instagram post due to parsing error: {innerEx.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Instagram data: {ex.Message}");
                throw;
            }

            return feedbackList;
        }


        public Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            return null;
        }
    }
}
