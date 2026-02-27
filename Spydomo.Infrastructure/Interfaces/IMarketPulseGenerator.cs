using Spydomo.DTO.MarketPulse;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IMarketPulseGenerator
    {
        Task<MarketPulseViewModel> GenerateAsync(CompanyGroup group, int timeWindowDays, CancellationToken ct);
    }
}
