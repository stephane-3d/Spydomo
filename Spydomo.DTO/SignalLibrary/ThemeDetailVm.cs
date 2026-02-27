using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.DTO.SignalLibrary
{
    public sealed class ThemeDetailVm
    {
        public string CategorySlug { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string SignalTypeSlug { get; set; } = "";
        public string SignalTypeName { get; set; } = "";
        public string ThemeSlug { get; set; } = "";
        public string ThemeName { get; set; } = "";
        public string? ThemeDescription { get; set; }

        public int Last30Count { get; set; }
        public decimal DeltaPct { get; set; }

        public List<ThemeExampleRow> Examples { get; set; } = new();
    }
}
