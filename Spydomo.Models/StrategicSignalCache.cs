namespace Spydomo.Models
{
    public class StrategicSignalCache
    {
        public int Id { get; set; }

        // Null for global cache
        public int? GroupId { get; set; }

        // e.g., "trend_evaluator", "competitor_comparison"
        public string Source { get; set; } = "";

        public string ContentJson { get; set; } = "";

        public DateTime GeneratedOn { get; set; }
    }


}
