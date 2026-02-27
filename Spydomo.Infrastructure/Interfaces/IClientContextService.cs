using Spydomo.DTO;
namespace Spydomo.Infrastructure.Interfaces
{
    public interface IClientContextService
    {
        Task<int> GetCurrentClientIdAsync();
        Task<int> GetCurrentUserIdAsync();
        Task<CompanyQuotaStatus> GetCompanyQuotaStatusAsync();
    }
}
