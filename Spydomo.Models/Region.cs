namespace Spydomo.Models;

public partial class Region
{
    public string Code { get; set; } = null!;

    public string? Name { get; set; }

    public string? CountryCode { get; set; }

    public virtual ICollection<Client> Clients { get; set; } = new List<Client>();

    public virtual Country? CountryCodeNavigation { get; set; }
}
