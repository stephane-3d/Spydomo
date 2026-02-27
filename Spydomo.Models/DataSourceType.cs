namespace Spydomo.Models;

public partial class DataSourceType
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? UrlKeywords { get; set; }

    public virtual ICollection<DataSource> DataSources { get; set; } = new List<DataSource>();

    public virtual ICollection<SummarizedInfo> SummarizedInfos { get; set; } = new List<SummarizedInfo>();

    public virtual ICollection<RawContent> UserFeedbacks { get; set; } = new List<RawContent>();
}
