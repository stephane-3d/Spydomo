using Spydomo.Common.Enums;

namespace Spydomo.Models;

public partial class Client
{
    public Client()
    {
        IsTrial = true;
        TrialEndsAt = DateTime.UtcNow.AddDays(14);
        Status = ClientStatus.TrialActive;
    }

    public int Id { get; set; }

    public string? Name { get; set; }
    public string? CountryCode { get; set; }
    public string? RegionCode { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? AddressLine1 { get; set; }

    public DateTime? DateCreated { get; set; }
    public DateTime? DeletionRequestedAt { get; set; }
    public DateTime? CleanupCompletedAt { get; set; }

    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? BillingEmail { get; set; }

    // 🔐 Stripe
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripeSubscriptionStatus { get; set; }

    // 📦 Plan
    public int PlanCompaniesCount { get; set; } = 3; // Default plan allows tracking 3 companies - free trial
    public int TrackedCompaniesCount { get; set; } = 0;

    // 📅 Billing Lifecycle
    public DateTime? SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionNextBillingDate { get; set; }
    public DateTime? SubscriptionCancelledAt { get; set; }

    // 🧪 Trial (Optional)
    public bool IsTrial { get; set; } = true;
    public DateTime? TrialEndsAt { get; set; } = DateTime.UtcNow.AddDays(14); // Default 14-day trial period
    public ClientStatus Status { get; set; } = ClientStatus.TrialActive;


    public virtual Country? CountryCodeNavigation { get; set; }
    public virtual Region? RegionCodeNavigation { get; set; }
    public virtual ICollection<TrackedCompany> TrackedCompanies { get; set; } = new List<TrackedCompany>();
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
