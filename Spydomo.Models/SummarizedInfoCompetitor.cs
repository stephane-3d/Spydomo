namespace Spydomo.Models;

public partial class SummarizedInfoCompetitor
{
    public int Id { get; set; }

    public DateTime? Date { get; set; }

    public int? CompetitorId { get; set; }

    public int? SummarizedInfoId { get; set; }

    public virtual Competitor? Competitor { get; set; }

    public virtual SummarizedInfo? SummarizedInfo { get; set; }
}
