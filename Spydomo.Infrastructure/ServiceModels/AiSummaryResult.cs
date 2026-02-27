namespace Spydomo.Infrastructure.ServiceModels
{
    public class AiSummaryResult
    {
        public string Gist { get; set; }
        public List<string> Points { get; set; }
        public Dictionary<string, string> Themes { get; set; }
        public Dictionary<string, string> Tags { get; set; }
        public (string Label, string Reason) Category { get; set; }
        public (string Label, string Reason) Sentiment { get; set; }
        public List<(int SignalTypeId, string Reason)> SignalTypes { get; set; } = new();

    }

}
