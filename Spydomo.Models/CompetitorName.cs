namespace Spydomo.Models;

public partial class CompetitorName
{
    public int Id { get; set; }

    public int CompetitorId { get; set; }

    public string Label { get; set; }

    public virtual Competitor? Competitor { get; set; }
}
