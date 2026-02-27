using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompanyRelationsService
    {
        Task<int> CreateRunAsync(int companyId, string query, string promptVersion, string rawJson, string parsedJson, CancellationToken ct = default);
        Task UpsertCandidatesAsync(int companyId, int runId, IEnumerable<CompanyCandidateDto> candidates, CompanyRelationSource source, CancellationToken ct = default);
        Task<List<CompanyRelation>> GetRelationsAsync(int companyId, CompanyRelationStatus minStatus, int take, CancellationToken ct = default);
    }
}
