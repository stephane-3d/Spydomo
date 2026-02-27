using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.DTO.SignalLibrary
{
    public sealed class ThemeRow
    {
        public string ThemeSlug { get; set; } = "";
        public string ThemeName { get; set; } = "";
        public string? Description { get; set; }
        public int Last30Count { get; set; }
        public int Prev30Count { get; set; }
        public decimal DeltaPct { get; set; }
    }
}
