using Spydomo.Common.Enums;

namespace Spydomo.DTO
{
    public class StrategicSignalDto
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public string CompanyName { get; set; }

        public string Gist { get; set; }

        public List<string> Tags { get; set; } = new();

        public string SourceType { get; set; } // "UserGenerated" or "CompanyMove"

        // Replace single SignalType with a list (if needed)
        public List<string> Types { get; set; } = new();

        public List<string> ThemeList { get; set; }

        // Strongly-typed Tier instead of "Importance"
        public PulseTier? Tier { get; set; }

        public string? TierReason { get; set; }

        public string? Url { get; set; }

        public DateTime CreatedOn { get; set; } // Optional: rename from DatePublished

        public string? PeriodType { get; set; } // "daily", "weekly", "warmup"
    }

}
