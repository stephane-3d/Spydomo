namespace Spydomo.DTO.Datasheet
{
    public sealed class OverviewRow
    {
        public int Id { get; set; }
        public string Group { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";

        public string Category { get; set; } = "";       // display name
        public string? CategorySlug { get; set; }        // stable identifier

        public decimal CategoryConfidence { get; set; }
        public string? CategoryReason { get; set; }
        public List<List<string>> CategoryEvidence { get; set; } = new();

        public string? SelfPositioning { get; set; }
        public string? SelfTitle { get; set; }
        public string? SelfDescription { get; set; }

        public List<string> Personas { get; set; } = new();
        public List<string> Segments { get; set; } = new();
    }

}
