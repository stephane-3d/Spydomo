namespace Spydomo.DTO
{
    public class SignalQueryParams
    {
        public int? GroupId { get; set; }
        public string Company { get; set; }
        public string Source { get; set; }
        public string Theme { get; set; }
        public string SignalType { get; set; }
        public string Importance { get; set; }
        public int PeriodDays { get; set; }
    }

}
