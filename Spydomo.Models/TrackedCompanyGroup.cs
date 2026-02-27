namespace Spydomo.Models
{
    public class TrackedCompanyGroup
    {
        public int TrackedCompanyId { get; set; }
        public int CompanyGroupId { get; set; }

        public TrackedCompany TrackedCompany { get; set; } = default!;
        public CompanyGroup CompanyGroup { get; set; } = default!;
    }

}
