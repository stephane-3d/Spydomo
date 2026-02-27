using Spydomo.Common.Constants;
using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.ServiceModels
{
    public record PulsePoint(
        int CompanyId,
        string CompanyName,
        PulseBucket Bucket,
        string ChipSlug,         // your existing classification
        PulseTier Tier,
        string Title,            // terse, human-readable summary seed (fallback if LLM fails)
        string Url,
        DateTime SeenAt,
        Dictionary<string, object?> Context, // stars, theme, baseline counts, deltas...
        int? RawContentId = null,
        int? SummarizedInfoId = null,
        string? SourceKey = null
    );
}
