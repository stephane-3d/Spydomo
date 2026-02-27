using Spydomo.Infrastructure.ServiceModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IPerplexityCompanyLandscapeClient
    {
        Task<CompanyLandscapeResponse> GetLandscapeAsync(
            string companyName,
            string companyUrl,
            int limit = 10,
            int? companyId = null,
            CancellationToken ct = default);
    }
}
