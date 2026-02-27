using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.ServiceModels
{
    public sealed class CompanyLandscapeResponse
    {
        public List<CompanyLandscapeCandidate> companies { get; set; } = new();
    }
}
