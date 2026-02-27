using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure.Parsers
{
    public class RedditParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.Reddit;

        private readonly IRelevanceEvaluator _relevanceEvaluator;
        public RedditParser(IRelevanceEvaluator relevanceEvaluator)
        {
            _relevanceEvaluator = relevanceEvaluator;
        }

        public async Task<List<RawContent>> Parse(string jsonResponse, int companyId, DataSource source, DateTime? lastUpdate, OriginTypeEnum originType = OriginTypeEnum.UserGenerated)
        {
            var feedbackList = new List<RawContent>();

            if (string.IsNullOrWhiteSpace(jsonResponse))
                return feedbackList;

            try
            {
                var redditJson = JsonSerializer.Deserialize<List<RedditSearchResult>>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (redditJson == null || redditJson.Count < 1)
                {
                    Console.WriteLine("Unexpected Reddit JSON structure.");
                    return feedbackList;
                }

                var firstListing = redditJson[0];
                if (firstListing?.Data?.Children == null || firstListing.Data.Children.Count == 0)
                {
                    Console.WriteLine("No Reddit post found in the first block.");
                    return feedbackList;
                }

                var postWrapper = redditJson[0].Data.Children.FirstOrDefault();
                if (postWrapper?.Data == null)
                {
                    Console.WriteLine("Reddit post has no content.");
                    return feedbackList;
                }

                var post = postWrapper.Data;
                var createdUtc = DateTimeOffset.FromUnixTimeSeconds((long)post.Created_Utc).UtcDateTime;

                var permalink = "https://www.reddit.com" + post.Permalink;

                var contentBuilder = new StringBuilder();
                contentBuilder.AppendLine(post.Title);
                contentBuilder.AppendLine();
                contentBuilder.AppendLine(post.Selftext);

                var content = contentBuilder.ToString().Trim();

                bool isRelevant = await _relevanceEvaluator.IsContentRelevantAsync(
                    companyId, content, DataSourceTypeEnum.Reddit);

                if (!isRelevant)
                {
                    Console.WriteLine($"❌ Skipping irrelevant Reddit post for companyId={companyId}");
                    return feedbackList;
                }


                if (redditJson.Count > 1 && redditJson[1]?.Data?.Children != null)
                {
                    var comments = redditJson[1].Data.Children
                        .Where(c => !string.IsNullOrWhiteSpace(c.Data?.Selftext))
                        .OrderByDescending(c => c.Data?.Score ?? 0)
                        .Take(3)
                        .Select(c => c.Data?.Selftext);

                    foreach (var comment in comments)
                    {
                        contentBuilder.AppendLine();
                        contentBuilder.AppendLine("Comment:");
                        contentBuilder.AppendLine(comment);
                    }
                }

                var structuredContent = new
                {
                    Title = TextHelper.CleanAndNormalize(post.Title),
                    Body = TextHelper.CleanAndNormalize(post.Selftext),
                    Comments = redditJson.Count > 1 && redditJson[1]?.Data?.Children != null
                        ? redditJson[1].Data.Children
                            .Where(c => !string.IsNullOrWhiteSpace(c.Data?.Selftext))
                            .OrderByDescending(c => c.Data?.Score ?? 0)
                            .Take(3)
                            .Select(c => c.Data?.Selftext)
                            .ToList()
                        : new List<string>(),

                    Metadata = new
                    {
                        Score = post.Score,
                        NumComments = post.Num_Comments,
                        Subreddit = post.Subreddit
                    }
                };


                var contentJson = JsonSerializer.Serialize(structuredContent, new JsonSerializerOptions
                {
                    WriteIndented = false
                });

                var engagementScore = post.Score + post.Num_Comments * 2;

                var rawContent = new RawContent
                {
                    CompanyId = companyId,
                    PostedDate = createdUtc,
                    PostUrl = permalink,
                    Content = contentJson,
                    DataSourceTypeId = (int)DataSourceTypeEnum.Reddit,
                    Status = RawContentStatusEnum.NEW,
                    CreatedAt = DateTime.UtcNow,
                    RawResponse = jsonResponse,
                    OriginType = originType,
                    EngagementScore = engagementScore
                };

                feedbackList.Add(rawContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Reddit data: {ex.Message}");
                throw;
            }

            return feedbackList;
        }


        public async Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            return null;
        }
    }
}
