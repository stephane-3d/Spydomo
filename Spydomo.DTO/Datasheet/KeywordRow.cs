namespace Spydomo.DTO.Datasheet
{
    public sealed class KeywordRow
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string Keyword { get; set; } = default!;
        public double Confidence { get; set; } = 1.0;
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
