using Spydomo.DTO.MarketPulse;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IMarketPulseService
    {
        /// <summary>
        /// Returns the cached Market Pulse VM for a public group (or generates + caches it).
        /// </summary>
        Task<MarketPulseViewModel?> GetPulseAsync(
            string groupSlug,
            bool forceRefresh = false,
            CancellationToken ct = default);

        Task<GroupHeaderDto?> GetGroupHeaderAsync(
            string slug,
            CancellationToken ct = default);
    }

}
