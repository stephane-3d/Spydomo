using Spydomo.Models;

namespace Spydomo.Infrastructure.ServiceModels
{
    public sealed record PulseAgentContext(
        int GroupId,
        IReadOnlyList<SummarizedInfo> SummarizedInfos,
        IReadOnlyList<PulsePoint> CandidatePulsePoints,
        DateTime PeriodStartUtc,
        DateTime PeriodEndUtc
    );

}
