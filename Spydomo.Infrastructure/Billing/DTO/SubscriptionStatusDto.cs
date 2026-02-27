namespace Spydomo.Infrastructure.Billing.DTO
{
    public class SubscriptionStatusDto
    {
        public string Status { get; set; } = "inactive";
        public int PlanCompaniesCount { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public DateTime? CurrentPeriodStart { get; set; }
        public int TrackedCount { get; set; }
        public bool CancelAtPeriodEnd { get; set; }
        public DateTime? CancelAt { get; set; }
    }

}
