using Spydomo.Infrastructure.Billing.DTO;
using Spydomo.Models;
using Stripe;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISubscriptionService
    {
        Task<SubscriptionStatusDto?> GetStatusAsync(Client client, CancellationToken ct = default);
        Task<string> StartSubscriptionAsync(int clientId, int quantity, CancellationToken ct = default); // Returns checkout URL
        Task UpdateSubscriptionQuantityAsync(int clientId, int newQuantity, CancellationToken ct = default);
        Task CancelSubscriptionAsync(int clientId, CancellationToken ct = default);

        Task<List<InvoiceDto>> GetInvoicesAsync(string customerId);
        Task<string?> GetStripeCustomerIdAsync(string userEmail);
        void SyncClientFromStripeSubscription(Subscription stripeSub, Client client);
        Task RestartSubscriptionAsync(int clientId, CancellationToken ct = default);

        Task<string> CreateBillingPortalSessionAsync(int clientId, CancellationToken ct = default);

        Task<decimal> GetCustomerCreditAsync(int clientId, CancellationToken ct = default);

    }

}
