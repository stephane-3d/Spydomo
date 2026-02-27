namespace Spydomo.Models;

public class TrackedCompany
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Notes { get; set; }

    public DateTime? DateCreated { get; set; }

    public int ClientId { get; set; }

    public int CompanyId { get; set; }

    public virtual Client Client { get; set; } = default!;
    public virtual Company Company { get; set; } = default!;

    public virtual ICollection<TrackedCompanyGroup> TrackedCompanyGroups { get; set; } = new List<TrackedCompanyGroup>();
}

