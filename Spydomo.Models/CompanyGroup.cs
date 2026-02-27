using System.ComponentModel.DataAnnotations;

namespace Spydomo.Models
{
    public class CompanyGroup
    {
        public int Id { get; set; }
        public int ClientId { get; set; }

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? Context { get; set; }

        public string Slug { get; set; } = null!;
        public bool IsPrivate { get; set; } = true;

        public virtual Client? Client { get; set; }

        public virtual ICollection<TrackedCompanyGroup> TrackedCompanyGroups { get; set; } = new List<TrackedCompanyGroup>();
        public ICollection<GroupSnapshot> Snapshots { get; set; }
        = new List<GroupSnapshot>();
    }


}
