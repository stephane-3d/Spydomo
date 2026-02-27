using System.Text.Json.Serialization;

namespace Spydomo.Infrastructure.ServiceModels
{
    public class ReadabilityResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("postedDate")]
        public DateTime? PostedDate { get; set; }
    }
}
