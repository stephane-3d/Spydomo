namespace Spydomo.DTO.MarketPulse
{
    public sealed class MarketPulseCompanyCard
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public string CompanySlug { get; set; } = "";

        public string PositioningLine { get; set; } = "";
        public string PositioningTitle { get; set; } = "";

        // The one featured sentence
        public string Signal { get; set; } = "";
        public string WhyItMatters { get; set; } = "";

        // 1–2 chips, public-safe
        public List<string> SignalTypes { get; set; } = new();

        // “Discussion lately”: 2–3
        public List<string> Themes { get; set; } = new();

        // “Significant keywords”: 2–3
        public List<string> Tags { get; set; } = new();
    }
}
