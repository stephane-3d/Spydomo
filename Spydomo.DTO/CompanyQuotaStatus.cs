namespace Spydomo.DTO
{
    public class CompanyQuotaStatus
    {
        public int TotalAllowed { get; set; }
        public int CurrentlyTracked { get; set; }
        public int Remaining => TotalAllowed - CurrentlyTracked;
        public bool HasPlan => TotalAllowed > 0;
    }

}
