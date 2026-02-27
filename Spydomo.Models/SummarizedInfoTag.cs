namespace Spydomo.Models
{
    public class SummarizedInfoTag
    {
        public int Id { get; set; }

        public int SummarizedInfoId { get; set; }

        public string Label { get; set; } = null!; // Raw tag from GPT, e.g., "support+" or "reporting-"
        public string Reason { get; set; } = null!;

        public string Sentiment { get; set; } = null!; // e.g., "positive", "negative", "neutral"

        public bool IsReviewed { get; set; } = false; // Indicates if the theme has been reviewed by a human

        public int? CanonicalTagId { get; set; }   // Optional normalized FK
        public double ConfidenceScore { get; set; } = 1.0;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;


        public virtual SummarizedInfo SummarizedInfo { get; set; } = null!;
        public virtual CanonicalTag? CanonicalTag { get; set; }
    }
}
