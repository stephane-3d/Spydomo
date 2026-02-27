namespace Spydomo.Common.Enums
{
    public sealed class SignalQueryRow
    {
        public int CompanyId { get; set; }
        public DateTime Date { get; set; }            // non-null (we'll coalesce)
        public int? SourceTypeId { get; set; }
        public OriginTypeEnum OriginType { get; set; }
        public string? PostUrl { get; set; }
        public int EngagementScore { get; set; }      // non-null (coalesce to 0)
        public string? Gist { get; set; }
        public SentimentEnum? Sentiment { get; set; }
        public int SignalScore { get; set; }
        public string? GistPointsJson { get; set; }
        public int? SummarizedInfoId { get; set; }    // nullable (raw-only rows have none)
    }
}
