using Microsoft.Extensions.Configuration;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure.Parsers
{
    public class G2Parser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.G2;
        private readonly IBrightDataService _brightDataService;
        private readonly string _datasetId; // G2 Dataset ID

        public G2Parser(IBrightDataService brightDataService, IConfiguration configuration)
        {
            _brightDataService = brightDataService;
            _datasetId = configuration["BrightData:DatasetIds:G2"];
        }

        public async Task<List<RawContent>> Parse(string jsonResponse, int companyId, DataSource source, DateTime? lastUpdate, OriginTypeEnum originType = OriginTypeEnum.UserGenerated)
        {
            var feedbackList = new List<RawContent>();

            if (string.IsNullOrEmpty(jsonResponse))
                return feedbackList;

            try
            {
                var reviews = JsonDocument.Parse(jsonResponse).RootElement;

                if (reviews.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("Expected JSON array at root but got something else.");
                    return feedbackList;
                }

                foreach (var review in reviews.EnumerateArray())
                {
                    try
                    {
                        // Validate and extract stars
                        double stars = review.TryGetProperty("stars", out var starsProp) && starsProp.ValueKind == JsonValueKind.Number
                            ? starsProp.GetDouble()
                            : 0;

                        // Validate and extract text array
                        string[] textArray = review.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.Array
                            ? textProp.EnumerateArray().Select(t => t.GetString()).Where(s => s != null).ToArray()
                            : Array.Empty<string>();

                        var contentObj = new
                        {
                            Text = string.Join("\n\n", textArray),
                            Metadata = new
                            {
                                Rating = stars
                            }
                        };

                        string textJson = JsonSerializer.Serialize(contentObj);

                        // Validate and extract required fields
                        if (!review.TryGetProperty("date", out var dateProp) || dateProp.ValueKind != JsonValueKind.String ||
                            !review.TryGetProperty("review_url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String)
                        {
                            Console.WriteLine("Skipping review due to missing date or review_url.");
                            continue;
                        }

                        var date = dateProp.GetDateTime();

                        var feedback = new RawContent
                        {
                            CompanyId = companyId,
                            PostedDate = date,
                            PostUrl = urlProp.GetString(),
                            Content = textJson,
                            DataSourceTypeId = (int)DataSourceTypeEnum.G2,
                            Status = RawContentStatusEnum.NEW,
                            CreatedAt = DateTime.UtcNow,
                            RawResponse = review.GetRawText(),
                            OriginType = OriginTypeEnum.UserGenerated
                        };

                        feedbackList.Add(feedback);
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"Skipping one review due to parsing error: {innerEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing G2 reviews: {ex.Message}");
                throw new Exception($"Error parsing G2 reviews: {ex.Message}");
            }

            return feedbackList;
        }


        public async Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            if (lastUpdate.HasValue && lastUpdate.Value > DateTime.UtcNow.AddDays(-7))
            {
                Console.WriteLine($"Skipping scrape, recent data available for {url}");
                return null;
            }

            return await _brightDataService.TriggerScrapingAsync(_datasetId, url, 1, "Most Recent");
        }
    }
}
