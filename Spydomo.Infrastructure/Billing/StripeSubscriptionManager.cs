using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Billing.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Stripe;
using Stripe.Checkout;

namespace Spydomo.Infrastructure.Billing
{
    public class StripeSubscriptionManager : ISubscriptionService
    {
        private readonly ILogger<StripeSubscriptionManager> _logger;
        private readonly IConfiguration _config;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IStripeClient _stripe;

        public StripeSubscriptionManager(ILogger<StripeSubscriptionManager> logger, IConfiguration config, IDbContextFactory<SpydomoContext> dbFactory, IStripeClient stripe)
        {
            _logger = logger;
            _config = config;
            _dbFactory = dbFactory;
            _stripe = stripe;
        }

        public async Task<SubscriptionStatusDto?> GetStatusAsync(Client client, CancellationToken ct = default)
        {
            var customerId = client.StripeCustomerId;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            if (string.IsNullOrWhiteSpace(customerId))
            {
                if (string.IsNullOrWhiteSpace(client.BillingEmail) && string.IsNullOrWhiteSpace(client.ContactEmail))
                    return null;

                var email = client.BillingEmail ?? client.ContactEmail;

                var customerService = new CustomerService(_stripe);
                var customers = await customerService.ListAsync(new CustomerListOptions { Email = email, Limit = 1 });
                var customer = customers.FirstOrDefault();
                if (customer == null) return null;

                customerId = customer.Id;

                // optional: persist so you never do this again
                client.StripeCustomerId = customerId;
                await db.SaveChangesAsync();
            }

            var subService = new SubscriptionService(_stripe);
            var subs = await subService.ListAsync(new SubscriptionListOptions { Customer = customerId, Limit = 10 });

            // Prefer active/trialing first; “canceled” is usually historical
            var sub = subs.FirstOrDefault(s => s.Status is "active" or "trialing")
                   ?? subs.FirstOrDefault(s => s.Status is "canceled");

            if (sub == null) return null;

            var item = sub.Items.Data.FirstOrDefault();

            return new SubscriptionStatusDto
            {
                Status = sub.Status,
                PlanCompaniesCount = (int)(item?.Quantity ?? 0),
                NextBillingDate = item?.CurrentPeriodEnd,
                CurrentPeriodStart = item?.CurrentPeriodStart,
                TrackedCount = client.TrackedCompaniesCount,
                CancelAtPeriodEnd = sub.CancelAtPeriodEnd,
                CancelAt = sub.CancelAt
            };
        }


        public async Task<List<InvoiceDto>> GetInvoicesAsync(string customerId)
        {
            var invoiceService = new InvoiceService(_stripe);

            var options = new InvoiceListOptions
            {
                Limit = 100,
                Expand = new List<string> { "data.customer" },
                Status = "paid"
            };

            if (!string.IsNullOrWhiteSpace(customerId))
            {
                options.Customer = customerId;
            }
            else
            {
                // Optional: log warning or return empty list
                return new List<InvoiceDto>();
            }

            var invoices = await invoiceService.ListAsync(options);

            return invoices.Data.Select(i => new InvoiceDto
            {
                Date = i.Created,
                Amount = (i.AmountPaid > 0 ? i.AmountPaid / 100m : (decimal)i.EndingBalance / 100m),
                Status = i.Status,
                PdfUrl = i.InvoicePdf
            }).ToList();
        }

        public async Task<string?> GetStripeCustomerIdAsync(string userEmail)
        {
            var customerService = new CustomerService(_stripe);
            var customers = await customerService.ListAsync(new CustomerListOptions
            {
                Email = userEmail,
                Limit = 1
            });

            return customers.FirstOrDefault()?.Id;
        }

        public async Task<string> StartSubscriptionAsync(int clientId, int quantity, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var client = await db.Clients.FindAsync(clientId);
            if (client == null)
                throw new Exception("Client not found.");

            var customerService = new CustomerService(_stripe);
            var customerId = client.StripeCustomerId;

            if (string.IsNullOrEmpty(customerId))
            {
                var customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = client.BillingEmail ?? client.ContactEmail,
                    Name = client.Name,
                    Address = new AddressOptions
                    {
                        Line1 = client.AddressLine1 ?? "N/A",
                        City = client.City,
                        State = client.RegionCode,
                        PostalCode = client.PostalCode,
                        Country = client.CountryCode
                    }
                });

                customerId = customer.Id;
                client.StripeCustomerId = customerId;
                client.Status = ClientStatus.SubscriptionActive;

                await db.SaveChangesAsync(); // only StripeCustomerId changes now
            }

            var sessionService = new SessionService(_stripe);
            var session = await sessionService.CreateAsync(new SessionCreateOptions
            {
                Customer = customerId,
                PaymentMethodTypes = new List<string> { "card" },
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = _config["Stripe:TrackedCompanyPriceId"],
                        Quantity = quantity
                    }
                },
                AutomaticTax = new SessionAutomaticTaxOptions
                {
                    Enabled = true
                },
                SuccessUrl = _config["App:BaseUrl"] + "/app/subscription?checkout=success&session_id={CHECKOUT_SESSION_ID}",
                CancelUrl = _config["App:BaseUrl"] + "/app/subscription?checkout=cancel"
            });

            return session.Url;
        }

        public async Task CancelSubscriptionAsync(int clientId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var client = await db.Clients.FindAsync(clientId);
            if (client == null || string.IsNullOrWhiteSpace(client.StripeSubscriptionId))
                throw new Exception("Client or subscription not found.");

            var service = new SubscriptionService(_stripe);
            var updatedSub = await service.UpdateAsync(client.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            });

            var canceledSub = await service.GetAsync(client.StripeSubscriptionId);
            SyncClientFromStripeSubscription(canceledSub, client);

            client.Status = ClientStatus.SubscriptionPendingCancel;
            client.SubscriptionCancelledAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        public async Task RestartSubscriptionAsync(int clientId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var client = await db.Clients.FindAsync(clientId);
            if (client == null || string.IsNullOrWhiteSpace(client.StripeSubscriptionId))
                throw new Exception("Client or subscription not found.");

            var service = new SubscriptionService(_stripe);
            var updatedSub = await service.UpdateAsync(client.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = false
            });

            var refreshedSub = await service.GetAsync(client.StripeSubscriptionId);
            SyncClientFromStripeSubscription(refreshedSub, client);

            client.Status = ClientStatus.SubscriptionActive;
            client.SubscriptionCancelledAt = null;

            await db.SaveChangesAsync();
        }

        public async Task UpdateSubscriptionQuantityAsync(int clientId, int newQuantity, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var client = await db.Clients.FindAsync(clientId);
            if (client == null || string.IsNullOrWhiteSpace(client.StripeSubscriptionId))
                throw new Exception("Client or subscription not found.");

            var subService = new SubscriptionService(_stripe);
            var sub = await subService.GetAsync(client.StripeSubscriptionId);

            var item = sub.Items.Data.FirstOrDefault();
            if (item == null)
                throw new Exception("Subscription item not found.");

            var currentQuantity = client.PlanCompaniesCount;
            var isUpgrade = newQuantity > currentQuantity;

            // ✅ Single call: update subscription, change item quantity, control proration behavior
            var updateOptions = new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = item.Id,
                        Quantity = newQuantity
                    }
                },

                // Upgrades: invoice prorations immediately (Stripe creates/finalizes invoice and attempts payment)
                // Downgrades: no proration invoice created
                ProrationBehavior = isUpgrade ? "always_invoice" : "none"
            };

            var updatedSub = await subService.UpdateAsync(client.StripeSubscriptionId, updateOptions);

            // Update internal state
            client.PlanCompaniesCount = newQuantity;
            SyncClientFromStripeSubscription(updatedSub, client);

            await db.SaveChangesAsync();
        }

        public async Task<decimal> GetCustomerCreditAsync(int clientId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var client = await db.Clients.FindAsync(clientId);
            if (client == null || string.IsNullOrWhiteSpace(client.StripeCustomerId))
                return 0;

            var customerService = new CustomerService(_stripe);
            var customer = await customerService.GetAsync(client.StripeCustomerId);

            // Stripe stores credits as negative balances
            var invoiceCreditInCents = customer.Balance < 0 ? -customer.Balance : 0;

            return invoiceCreditInCents / 100m;
        }


        public async Task<string> CreateBillingPortalSessionAsync(int clientId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var client = await db.Clients.FindAsync(clientId);
            if (client == null || string.IsNullOrWhiteSpace(client.StripeCustomerId))
                throw new Exception("Client or Stripe customer ID not found.");

            var domain = _config["App:BaseUrl"];
            var portalService = new Stripe.BillingPortal.SessionService(_stripe);

            var session = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = client.StripeCustomerId,
                ReturnUrl = $"{domain}/app/subscription"
            });

            return session.Url;
        }

        public void SyncClientFromStripeSubscription(Subscription stripeSub, Client client)
        {
            var item = stripeSub.Items?.Data?.FirstOrDefault();

            client.StripeSubscriptionId = stripeSub.Id;
            client.StripeSubscriptionStatus = stripeSub.Status;

            // Quantity
            client.PlanCompaniesCount = (int)(item?.Quantity ?? 0);

            // Start date (don’t overwrite every time)
            // In your Stripe.NET version these are DateTime (not nullable)
            if (client.SubscriptionStartDate == default || client.SubscriptionStartDate == DateTime.MinValue)
            {
                // Prefer StartDate if present in your model
                // (If StartDate doesn't exist in your version, just use Created)
                client.SubscriptionStartDate =
                    stripeSub.StartDate != default ? stripeSub.StartDate :
                    stripeSub.Created != default ? stripeSub.Created :
                    DateTime.UtcNow;
            }

            // Next billing date (in your version, use the item period end)
            client.SubscriptionNextBillingDate = item?.CurrentPeriodEnd;

            // Trial flags/dates
            var isTrialing = string.Equals(stripeSub.Status, "trialing", StringComparison.OrdinalIgnoreCase);
            client.IsTrial = isTrialing;

            // TrialEnd may or may not exist / may be nullable depending on version.
            // If yours is non-nullable DateTime:
            client.TrialEndsAt = isTrialing && stripeSub.TrialEnd != default ? stripeSub.TrialEnd : null;

            // If instead TrialEnd is nullable DateTime? in your version, use:
            // client.TrialEndsAt = isTrialing ? stripeSub.TrialEnd : null;

            client.Status = stripeSub.Status switch
            {
                "active" when stripeSub.CancelAtPeriodEnd => ClientStatus.SubscriptionPendingCancel,
                "active" => ClientStatus.SubscriptionActive,
                "trialing" => ClientStatus.TrialActive,
                "canceled" => ClientStatus.SubscriptionCancelled,
                "unpaid" => ClientStatus.SubscriptionUnpaid,
                _ => ClientStatus.Inactive
            };
        }

    }

}
