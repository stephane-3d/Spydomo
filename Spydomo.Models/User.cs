namespace Spydomo.Models;

public partial class User
{
    public int Id { get; set; }
    public string ClerkUserId { get; set; } = null!;

    public string? Name { get; set; }

    public string? Email { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? DateCreated { get; set; }

    public DateTime? LastVisit { get; set; }

    public int VisitsCount { get; set; } = 0;

    public string Role { get; set; } = "user"; // Default role

    public string InvitationStatus { get; set; } = "none"; // sent, accepted

    public int? ClientId { get; set; }

    public virtual Client? Client { get; set; }
}
