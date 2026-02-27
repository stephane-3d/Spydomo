using System.ComponentModel.DataAnnotations;

namespace Spydomo.Models
{
    public class SummarizedInfoSignalType
    {
        public int Id { get; set; }

        public int SignalTypeId { get; set; }
        public SignalType SignalType { get; set; } = null!;

        [MaxLength(512)]
        public string Reason { get; set; } = string.Empty;

        public int SummarizedInfoId { get; set; }
        public SummarizedInfo SummarizedInfo { get; set; } = null!;
    }
}
