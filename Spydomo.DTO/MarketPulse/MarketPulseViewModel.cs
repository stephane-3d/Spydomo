namespace Spydomo.DTO.MarketPulse
{
    public sealed class MarketPulseViewModel
    {
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public bool IsPublicPreview { get; set; } = true;
        public DateTime LastUpdatedUtc { get; set; }

        public List<MarketPulseCompanyCard> Companies { get; set; } = new();
        public List<RelatedGroupLinkDto> RelatedGroups { get; set; } = new();

    }
}
