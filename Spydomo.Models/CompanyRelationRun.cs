using Spydomo.Common.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Models
{
    public partial class CompanyRelationRun
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public virtual Company Company { get; set; } = default!;

        public CompanyRelationSource Provider { get; set; } = CompanyRelationSource.Perplexity;

        public string Query { get; set; } = default!;            // nvarchar(500)
        public string PromptVersion { get; set; } = "v1";         // nvarchar(50)

        public string? RawResponseJson { get; set; }              // nvarchar(max)
        public string? ParsedCandidatesJson { get; set; }         // nvarchar(max)

        public bool Success { get; set; } = true;
        public string? Error { get; set; }                        // nvarchar(800)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<CompanyRelation> Relations { get; set; } = new List<CompanyRelation>();
    }
}
