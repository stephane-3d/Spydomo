using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IPulseRule
    {
        bool IsMatch(SummarizedInfo si, TrackContext ctx);
        Task<PulsePoint?> ProjectAsync(SummarizedInfo si, TrackContext ctx, CancellationToken ct = default);
    }
}
