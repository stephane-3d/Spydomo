using Spydomo.Common.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.DTO
{
    public sealed record CompanyCandidateDto(
        string Name,
        string? Url,
        CompanyRelationType RelationType,
        decimal Confidence,
        string? Reason
    );
}
