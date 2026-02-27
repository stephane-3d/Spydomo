namespace Spydomo.Models;

public partial class Competitor
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public virtual ICollection<SummarizedInfoCompetitor> SummarizedInfoCompetitors { get; set; } = new List<SummarizedInfoCompetitor>();
}
