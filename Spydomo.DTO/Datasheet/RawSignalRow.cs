namespace Spydomo.DTO.Datasheet
{
    public sealed class RawSignalRow
    {
        public int CompanyId { get; set; }
        public string Company { get; set; } = "";
        public string Url { get; set; } = "";                  // RawContent.PostUrl (if any)
        public DateTime Date { get; set; }                     // si.Date ?? si.GistGeneratedAt ?? rc.PostedDate ?? rc.CreatedAt
        public string Source { get; set; } = "";               // DataSourceType display
        public int? SourceTypeId { get; set; }                 // for filtering
        public string Origin { get; set; } = "";               // "company" | "user"
        public int EngagementScore { get; set; }               // raw content metric (0 if n/a)

        // SummarizedInfo
        public string? Gist { get; set; }
        public string? Sentiment { get; set; }                 // "pos" | "neu" | "neg" | null
        public int SignalScore { get; set; }
        public List<string> GistPoints { get; set; } = new();

        // Multi-values
        public List<ReasonItem> Themes { get; set; } = new();  // Label + Reason
        public List<ReasonItem> Tags { get; set; } = new();    // Label + Reason
    }
}
