using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.DTO
{
    public sealed record PostingWindowStats(
        DateTime StartDate,
        DateTime EndDate,
        int CurrentPosts,
        int PreviousPosts,
        Dictionary<string, int> SourceBreakdown);
}
