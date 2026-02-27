using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompanyService
    {
        Task<TrackedCompanyDto> AddOrGetTrackedCompanyAsync(int clientId, string url,
            CancellationToken ct = default);
        Task<List<TrackedCompanyDto>> GetTrackedCompaniesForClientAsync(int clientId,
            int? groupId = null,
            CancellationToken ct = default);
        Task UpdateTrackedCompanyNoteAsync(int trackedCompanyId, string note,
            CancellationToken ct = default);
        Task RemoveTrackedCompanyAsync(int clientId, int trackedCompanyId,
            CancellationToken ct = default);
    }
}
