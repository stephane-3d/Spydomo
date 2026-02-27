using Spydomo.Common.Enums;
using System.ComponentModel.DataAnnotations;

namespace Spydomo.Models
{
    public class SnapshotJob
    {
        public int Id { get; set; }

        [Required]
        public string SnapshotId { get; set; }

        [Required]
        public int CompanyId { get; set; }

        public int DataSourceTypeId { get; set; }

        public DateTime TriggeredAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public string? DateFilter { get; set; }
        public string? TrackingData { get; set; }
        public string Status { get; set; }

        public OriginTypeEnum OriginType { get; set; } = OriginTypeEnum.UserGenerated; // <-- New

        // Optional navigation
        public Company Company { get; set; }
        public DataSourceType DataSourceType { get; set; }
    }


}
