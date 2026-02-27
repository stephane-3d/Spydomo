using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.DTO.SignalLibrary
{
    public sealed class ThemeExampleRow
    {
        public DateTime? PostedDate { get; set; }
        public string CompanyName { get; set; } = "";
        public string? Url { get; set; }
        public string? Gist { get; set; }

        public string? SignalReason { get; set; }
        
        public int? SummarizedInfoId { get; set; }
    }
}
