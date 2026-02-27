namespace Spydomo.Models
{
    public class SummarizedInfoTheme
    {
        public int Id { get; set; }

        public int SummarizedInfoId { get; set; }
        public int? CanonicalThemeId { get; set; }

        public string Label { get; set; } = null!;
        public string Reason { get; set; } = null!;

        public bool IsReviewed { get; set; } = false; // Indicates if the theme has been reviewed by a human

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;


        public double ConfidenceScore { get; set; } = 1.0;

        public virtual SummarizedInfo SummarizedInfo { get; set; } = null!;
        public virtual CanonicalTheme CanonicalTheme { get; set; } = null!;
    }


}
