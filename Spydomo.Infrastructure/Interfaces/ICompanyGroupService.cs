using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompanyGroupService
    {
        Task<CompanyGroupDto?> GetDefaultGroupForClientAsync(int clientId);
        Task<List<CompanyGroupDto>> GetCompanyGroupsForClientAsync(int clientId);
        Task<List<CompanyGroupDto>> GetPublicCompanyGroupsAsync(int clientId);
        Task<CompanyGroupDto> CreateCompanyGroupAsync(CompanyGroupDto dto);
        Task UpdateCompanyGroupAsync(CompanyGroupDto dto);
        Task DeleteCompanyGroupAsync(int groupId, int clientId);
        Task AssignCompanyToGroupAsync(int trackedCompanyId, int groupId);
        Task<List<int>> GetCompanyIdsForGroupAsync(int groupId);
        Task UpdateCompaniesForGroupAsync(int groupId, List<int> companyIds);
        Task RemoveCompanyFromGroupAsync(int trackedCompanyId, int groupId);
    }
}
