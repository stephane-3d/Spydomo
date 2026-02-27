namespace Spydomo.DTO
{
    public sealed record FormatterOut
    {
        public int CompanyId { get; init; }
        public string? CompanyName { get; init; }
        public string? Blurb { get; init; }
        public string? TierReason { get; init; }
        public int? RawContentId { get; init; }
        public int? SummarizedInfoId { get; init; }
        public string? Title { get; init; }   // optional, helps matching
        public string? Url { get; init; }     // optional; include if your prompt returns it
    }
}
