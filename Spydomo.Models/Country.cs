namespace Spydomo.Models;

public partial class Country
{
    public string Code { get; set; } = null!;

    public string? Name { get; set; }

    public string? Area { get; set; }

    public virtual ICollection<Client> Clients { get; set; } = new List<Client>();

    public virtual ICollection<Region> Regions { get; set; } = new List<Region>();
}
