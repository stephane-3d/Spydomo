namespace Spydomo.Infrastructure.ServiceModels
{
    public class RelevantSnippet
    {
        public string Text { get; set; } = string.Empty;
        public int Score { get; set; }
        public string Source { get; set; } = string.Empty; // e.g. "Title", "Comment"
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
