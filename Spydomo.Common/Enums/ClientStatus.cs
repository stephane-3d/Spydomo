namespace Spydomo.Common.Enums
{
    public enum ClientStatus
    {
        TrialActive,
        TrialExpired,
        SubscriptionActive,
        SubscriptionCancelled,
        SubscriptionUnpaid,
        SubscriptionPendingCancel,
        Inactive, // No subscription, no trial
        Deleted

    }

}
