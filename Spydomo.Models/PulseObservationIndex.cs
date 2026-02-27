namespace Spydomo.Models
{
    // Models/PulseObservationIndex.cs
    public class PulseObservationIndex
    {
        public long Id { get; set; }
        public int CompanyId { get; set; }

        // "Pain" | "FeatureRequest" | "Praise" (store as text for easy querying)
        public string Type { get; set; } = string.Empty;

        // canonical topic key: e.g., "widget-editing-friction"
        public string TopicKey { get; set; } = string.Empty;

        // UTC date bucket (date-only); use DateOnly with EF Core 7/8
        public DateOnly DateBucket { get; set; }

        // for stats/debug
        public DateTime FirstSeenAt { get; set; }     // first mention this day
        public DateTime LastSeenAt { get; set; }      // last mention this day
        public int Count { get; set; }                // mentions this day

        public Company? Company { get; set; }
    }

}
