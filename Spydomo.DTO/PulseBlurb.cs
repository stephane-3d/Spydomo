using Spydomo.Common.Constants;
using Spydomo.Common.Enums;

namespace Spydomo.DTO
{
    public record PulseBlurb(
        int CompanyId,
        string CompanyName,
        string Blurb,
        PulseTier Tier,
        string TierReason,
        int? RawContentId,
        int? SummarizedInfoId,
        string Url,
        string Chip,
        PulseBucket Bucket,
        string? SourceKey
    );


}
