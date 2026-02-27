using Spydomo.Common.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Models
{
    public partial class CompanyRelation
    {
        public int Id { get; set; }

        // Source company
        public int CompanyId { get; set; }
        public virtual Company Company { get; set; } = default!;

        // Target company (nullable until resolved)
        public int? RelatedCompanyId { get; set; }
        public virtual Company? RelatedCompany { get; set; }

        // Raw fields (when we can’t resolve immediately)
        public string? RelatedCompanyNameRaw { get; set; }   // nvarchar(200)
        public string? RelatedCompanyUrlRaw { get; set; }    // nvarchar(500)
        public string? RelatedDomain { get; set; }           // nvarchar(200) normalized domain

        public CompanyRelationType RelationType { get; set; }
        public CompanyRelationStatus Status { get; set; } = CompanyRelationStatus.Proposed;
        public CompanyRelationSource Source { get; set; }

        public decimal Confidence { get; set; }              // decimal(4,3) 0..1
        public int EvidenceCount { get; set; }               // starts at 1
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }

        // provenance
        public int? RunId { get; set; }
        public virtual CompanyRelationRun? Run { get; set; }

        // optional “why”
        public string? Reason { get; set; }                  // nvarchar(400)
    }
}
