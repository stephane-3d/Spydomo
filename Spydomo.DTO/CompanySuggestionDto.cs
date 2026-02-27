using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.DTO
{
    public sealed record CompanySuggestionDto(
        int CompanyId,
        string Name,
        string? Url,
        string ReasonLabel,     // "Often tracked with", "Competitive landscape"
        decimal Score
    );
}
