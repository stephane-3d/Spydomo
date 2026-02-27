using Spydomo.Infrastructure.ServiceModels;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompanyContextExtractor
    {
        Task<CompanyContextResult> ExtractContextAsync(string visibleText, int? companyId = null);
    }
}
