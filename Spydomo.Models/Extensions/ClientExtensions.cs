using Spydomo.Common.Enums;

namespace Spydomo.Models.Extensions
{
    public static class ClientExtensions
    {
        public static bool IsActive(this Client client)
        {
            return client.Status == ClientStatus.SubscriptionActive || client.Status == ClientStatus.TrialActive;
        }

        public static bool IsTrialExpired(this Client client)
        {
            return client.Status == ClientStatus.TrialExpired;
        }
    }
}
