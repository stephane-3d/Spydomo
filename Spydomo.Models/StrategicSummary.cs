using Spydomo.Common.Enums;

namespace Spydomo.Models
{
    public class StrategicSummary
    {
        public int Id { get; set; }

        public int CompanyGroupId { get; set; }
        public CompanyGroup CompanyGroup { get; set; }

        public int? CompanyId { get; set; }
        public Company Company { get; set; }
        public string PeriodType { get; set; } = "daily"; // optional: daily, weekly, etc.

        public string? SourceKey { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;

        public List<string> IncludedSignalTypes { get; set; } = new(); // now slugs

        public int? RawContentId { get; set; }
        public int? SummarizedInfoId { get; set; }
        public string Url { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public PulseTier? Tier { get; set; } // e.g. "Tier 1", "Tier 2", "Tier 3"
        public string? TierReason { get; set; } // Explanation of why it belongs to this tier
    }


}
