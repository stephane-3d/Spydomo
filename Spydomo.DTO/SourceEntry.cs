namespace Spydomo.DTO
{
    public sealed class SourceEntry
    {
        public int UserMentions { get; set; }
        public int CompanyPosts { get; set; }  // synonym of PostCount for company-authored
        public int PostCount { get; set; }     // keep for clarity
        public double AvgEngagement { get; set; }
        public double MedianEngagement { get; set; }
        public MediaBreakdown Media { get; set; } = new();
    }
}
