using Spydomo.Common.Enums;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ITrackProcessor
    {
        string Name { get; }                   // "Reviews", "Community", "CompanyContent", ...
        PulseBucket Bucket { get; }            // e.g., CustomerVoice, Marketing
        TrackContext BuildContext(int groupId, IEnumerable<SummarizedInfo> sis);

        // async stream of points
        IAsyncEnumerable<PulsePoint> EvaluateAsync(
            IEnumerable<SummarizedInfo> sis,
            TrackContext ctx,
            CancellationToken ct = default);
    }

}
