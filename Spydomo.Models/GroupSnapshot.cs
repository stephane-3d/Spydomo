using Spydomo.Common.Enums;

namespace Spydomo.Models
{
    public class GroupSnapshot
    {
        public int Id { get; set; }

        public int GroupId { get; set; }   // FK to CompanyGroup

        public string GroupSlug { get; set; } = default!;
        public int TimeWindowDays { get; set; } = 30;
        public GroupSnapshotKind Kind { get; set; } = GroupSnapshotKind.Pulse; // "arena" | "pulse"

        public int SchemaVersion { get; set; } = 1;

        public DateTime GeneratedAtUtc { get; set; }

        public string PayloadJson { get; set; } = default!;

        // Navigation
        public CompanyGroup Group { get; set; } = default!;
    }


}
