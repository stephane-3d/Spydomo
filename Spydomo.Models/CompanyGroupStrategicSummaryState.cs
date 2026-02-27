namespace Spydomo.Models
{
    public class CompanyGroupStrategicSummaryState
    {
        public int CompanyGroupId { get; set; }
        public int LastProcessedSummarizedInfoId { get; set; }  // watermark
        public DateTimeOffset? LastRunUtc { get; set; }

        // Optional: simple lock to avoid two workers doing same group
        public DateTimeOffset? LockedUntilUtc { get; set; }

        public CompanyGroup CompanyGroup { get; set; } = default!;
    }

}
