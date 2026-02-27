using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.ServiceModels
{
    public class StrategicSignal
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string? Gist { get; set; }
        public List<string> GistPoints { get; set; } = new();
        public List<string> Themes { get; set; }
        public List<string> Tags { get; set; }
        public DateTime DetectedOn { get; set; }
        public string TypeSlug { get; set; } = "";

        public double RelevanceScore { get; set; }
        public List<string> Sources { get; set; }
        public int? SummarizedInfoId { get; set; } // link to root
        public int? RawContentId { get; set; } // link to root
        public string? PostUrl { get; set; }
        public int? SignalScore { get; set; }
        public int? EngagementScore { get; set; }
        public string? CompanyName { get; set; }
        public string? Sentiment { get; set; }
        public List<string>? CompetitorMentions { get; set; }
    }

    public class CompetitiveSignal
    {
        public string Company { get; set; }
        public int CompanyId { get; set; }
        public List<SignalSummary> Summary { get; set; }
    }

    public class SignalSummary
    {
        public string Blurb { get; set; }
        public PulseTier Tier { get; set; }
        public string TierReason { get; set; }
        public SignalSource Source { get; set; }
    }

    public class SignalSource
    {
        public int? RawContentId { get; set; }
        public int? SummarizedInfoId { get; set; }
        public DateTime? PostDate { get; set; }
        public string Url { get; set; }
        public List<string> SignalTypes { get; set; } = new();
    }

}
