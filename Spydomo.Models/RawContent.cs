using Spydomo.Common.Enums;
using System.ComponentModel.DataAnnotations;

namespace Spydomo.Models;

public partial class RawContent
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int DataSourceTypeId { get; set; }

    public string Content { get; set; } = null!;

    private string? _postUrl;
    [MaxLength(500)]
    public string? PostUrl
    {
        get => _postUrl;
        set
        {
            if (value != null && value.Length > 500)
                _postUrl = value.Substring(0, 500);
            else
                _postUrl = value;
        }

    }

    public OriginTypeEnum OriginType { get; set; }

    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessingAt { get; set; }

    public DateTime? PostedDate { get; set; }
    public string? RawResponse { get; set; } = null!;
    [MaxLength(20)]
    public string? Status { get; set; }

    public float? ConfidenceScore { get; set; } = 1.0f; // Default for structured reviews
    public int? EngagementScore { get; set; } = 0;

    public virtual Company Company { get; set; } = null!;

    public virtual DataSourceType DataSourceType { get; set; } = null!;

}
