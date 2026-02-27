using Spydomo.DTO;
using Spydomo.Infrastructure.ServiceModels;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IPulseAgent
    {
        Task<IReadOnlyList<PulseBlurb>> GeneratePulsesAsync(
            PulseAgentContext context,
            CancellationToken ct = default);
    }
}
