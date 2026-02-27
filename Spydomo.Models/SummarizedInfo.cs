using Spydomo.Common.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Spydomo.Models;

public partial class SummarizedInfo
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int? SourceTypeId { get; set; }
    public SentimentEnum? Sentiment { get; set; }
    public string? SentimentReason { get; set; }

    public DateTime? Date { get; set; }

    public int SignalScore { get; set; }

    public string? Gist { get; set; } = null!;
    public DateTime? GistGeneratedAt { get; set; }
    public string? GistSource { get; set; } = "gpt-4"; // Optional default

    public string? GistPointsJson { get; set; }

    public SummarizedInfoProcessingStatus ProcessingStatus { get; set; } = SummarizedInfoProcessingStatus.New;

    public int? RawContentId { get; set; }

    public OriginTypeEnum OriginType { get; set; }

    public virtual Company? Company { get; set; }

    public virtual ICollection<SummarizedInfoCompetitor> SummarizedInfoCompetitors { get; set; } = new List<SummarizedInfoCompetitor>();

    public virtual DataSourceType? SourceType { get; set; }

    public virtual RawContent? RawContent { get; set; }

    public virtual ICollection<SummarizedInfoTag> SummarizedInfoTags { get; set; } = new List<SummarizedInfoTag>();
    public virtual ICollection<SummarizedInfoTheme> SummarizedInfoThemes { get; set; } = new List<SummarizedInfoTheme>();
    public virtual ICollection<SummarizedInfoSignalType> SummarizedInfoSignalTypes { get; set; } = new List<SummarizedInfoSignalType>();

    [NotMapped]
    public DataSourceTypeEnum? SourceTypeEnum =>
        SourceTypeId.HasValue && Enum.IsDefined(typeof(DataSourceTypeEnum), SourceTypeId.Value)
            ? (DataSourceTypeEnum)SourceTypeId.Value
            : null;

}
