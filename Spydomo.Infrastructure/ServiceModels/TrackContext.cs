using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure.ServiceModels
{
    public record TrackContext(
        int GroupId,
        IBaselineProvider Baselines,
        DateTime NowUtc
    );
}
