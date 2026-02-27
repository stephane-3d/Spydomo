using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.ServiceModels
{
    public sealed class CompanyLandscapeCandidate
    {
        public string name { get; set; } = "";
        public string url { get; set; } = "";
        public string relationType { get; set; } = "SameSpace"; // Competitor | Alternative | SameSpace | Adjacent
        public double confidence { get; set; }                  // 0..1
        public string reason { get; set; } = "";
        public List<string> evidence { get; set; } = new();     // exact phrases
    }
}
