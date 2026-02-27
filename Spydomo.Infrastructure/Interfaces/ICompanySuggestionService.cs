using Spydomo.DTO;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompanySuggestionService
    {
        Task<List<CompanySuggestionDto>> GetSuggestionsForNewCompanyAsync(
            int clientId,
            int seedCompanyId,
            int take = 6,
            CancellationToken ct = default);
    }
}
