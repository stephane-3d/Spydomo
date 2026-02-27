using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.DTO.SignalLibrary
{
    public sealed class SignalsIndexRow
    {
        public string CategorySlug { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string CategoryDescription { get; set; } = "";
        public int Last30Count { get; set; }
        public int Prev30Count { get; set; }
        public decimal DeltaPct { get; set; } // e.g. 0.12m
    }
}
