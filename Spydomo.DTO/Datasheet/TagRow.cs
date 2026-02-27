namespace Spydomo.DTO.Datasheet
{
    public sealed class TagRow
    {
        public string Label { get; set; } = "";
        public string? Reason { get; set; }
        public int Count { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string Sentiment { get; set; } = "neu"; // "pos" | "neu" | "neg"
    }

}
