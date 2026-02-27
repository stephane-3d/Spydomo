namespace Spydomo.Models;

public partial class DataSource
{
    public int Id { get; set; }

    public string? Url { get; set; }

    public DateTime? DateCreated { get; set; }

    public DateTime? LastUpdate { get; set; }

    public bool? IsActive { get; set; }

    public int? TypeId { get; set; }

    public int? CompanyId { get; set; }

    public virtual Company? Company { get; set; }

    public virtual DataSourceType? Type { get; set; }
}
