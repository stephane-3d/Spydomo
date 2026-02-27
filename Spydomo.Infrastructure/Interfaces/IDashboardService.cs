using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IDashboardService
    {
        Task<List<StrategicSignalDto>> GetSignalsAsync(SignalQueryParams query, int clientId);
    }
}
