namespace Spydomo.Models
{
    public class CompanyKeyword
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string Keyword { get; set; } = default!;
        public double Confidence { get; set; } = 1.0;
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Company Company { get; set; } = default!;
    }

}
