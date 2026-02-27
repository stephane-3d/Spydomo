namespace Spydomo.Infrastructure.ServiceModels
{
    public class SnippetPart
    {
        public string Text { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // e.g. "Title", "Body", "Comment"
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
