using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Stripe;

namespace Spydomo.Web.Controllers.Webhooks
{
    [ApiController]
    [Route("api/webhooks/stripe")]
    public class StripeWebhookController : ControllerBase
    {
        private readonly ILogger<StripeWebhookController> _logger;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IConfiguration _config;
        private readonly ISlackNotifier _slack;
        private readonly IEmailService _emailService;

        public StripeWebhookController(
            ILogger<StripeWebhookController> logger,
            IDbContextFactory<SpydomoContext> dbFactory,
            ISubscriptionService subscriptionService,
            IConfiguration config,
            IEmailService emailService,
            ISlackNotifier slack)
        {
            _logger = logger;
            _dbFactory = dbFactory;
            _subscriptionService = subscriptionService;
            _config = config;
            _emailService = emailService;
            _slack = slack;
        }

        [HttpPost]
        public async Task<IActionResult> Handle(CancellationToken ct)
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            Subscription? subscription = null;
            bool notify = false;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    _config["Stripe:WebhookSecret"]
                );

                switch (stripeEvent.Type)
                {
                    case "customer.subscription.updated":
                    case "customer.subscription.created":
                        {
                            subscription = stripeEvent.Data.Object as Subscription;
                            await SyncFromSubscription(subscription, ct);
                            notify = ShouldNotifyStripeEvent(stripeEvent);  // ✅ HERE
                            break;
                        }

                    case "customer.subscription.deleted":
                        {
                            subscription = stripeEvent.Data.Object as Subscription;
                            await SyncFromSubscription(subscription, ct);
                            notify = true; // delete always notifies
                            break;
                        }

                    case "invoice.payment_succeeded":
                        {
                            var invoice = stripeEvent.Data.Object as Invoice;

                            if (!string.IsNullOrWhiteSpace(invoice?.CustomerId))
                            {
                                var client = await db.Clients.FirstOrDefaultAsync(c => c.StripeCustomerId == invoice.CustomerId);
                                if (client != null && !string.IsNullOrWhiteSpace(client.StripeSubscriptionId))
                                {
                                    var subscriptionService = new SubscriptionService();
                                    var sub = await subscriptionService.GetAsync(client.StripeSubscriptionId);
                                    await SyncFromSubscription(sub, ct);
                                }
                                else
                                {
                                    var msg = $"No client found or subscription missing. StripeCustomerId: {invoice.CustomerId}";
                                    _logger.LogWarning(msg);
                                }
                            }

                            // Optional: do NOT notify here if you already notify on subscription updates.
                            // If you ever want a “payment succeeded” notification, do it explicitly as a different subject.
                            break;
                        }

                    case "invoice.payment_failed":
                        {
                            var failedInvoice = stripeEvent.Data.Object as Invoice;

                            if (!string.IsNullOrWhiteSpace(failedInvoice?.CustomerId))
                            {
                                var client = await db.Clients.FirstOrDefaultAsync(c => c.StripeCustomerId == failedInvoice.CustomerId);
                                if (client != null)
                                {
                                    client.Status = ClientStatus.SubscriptionUnpaid;
                                    await db.SaveChangesAsync();
                                    _logger.LogWarning($"Subscription marked as unpaid for client ID {client.Id} (CustomerId: {failedInvoice.CustomerId})");
                                }
                            }
                            break;
                        }

                    default:
                        {
                            var msgUnhandled = $"Unhandled Stripe event type: {stripeEvent.Type} Payload: {json}";
                            _logger.LogInformation(msgUnhandled);
                            break;
                        }
                }

                // ✅ Send notification only when it’s meaningful
                if (notify && subscription != null)
                {
                    var client = await GetClientFromSubscriptionAsync(subscription, ct);
                    if (client != null)
                        await NotifyClientChangeAsync(client, stripeEvent.Type);
                    else
                        _logger.LogWarning($"Stripe webhook: Unable to find client for subscription {subscription.Id} / customer {subscription.CustomerId}");
                }

                return Ok();
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe webhook processing failed. Payload: {json}");
                return BadRequest();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected webhook error. Payload: {json}");
                return BadRequest();
            }
        }

        private async Task SyncFromSubscription(Subscription stripeSub, CancellationToken ct)
        {
            var customerId = stripeSub.CustomerId;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var client = await db.Clients.FirstOrDefaultAsync(c => c.StripeCustomerId == customerId);
            if (client == null)
            {
                _logger.LogWarning($"No client found for customer {customerId}");
                return;
            }

            // Update client using your existing logic
            _subscriptionService.SyncClientFromStripeSubscription(stripeSub, client);

            await db.SaveChangesAsync();
        }

        private async Task NotifyClientChangeAsync(Client client, string eventType)
        {
            var subject = eventType switch
            {
                "customer.subscription.created" => "✅ New Subscription Started",
                "customer.subscription.updated" => "🔄 Subscription Updated",
                "customer.subscription.deleted" => "❌ Subscription Cancelled",
                _ => null
            };

            if (subject == null)
                return;

            var emailTo = _config["AcsEmail:DefaultReplyTo"]!;

            var message = $"Client: {client.Name} ({client.Id})\n" +
                          $"Event: {subject}\n" +
                          $"Tracked Companies: {client.PlanCompaniesCount} ({client.PlanCompaniesCount * 10}$)\n" +
                          $"Billing Email: {client.BillingEmail ?? client.ContactEmail}\n";

            await _slack.NotifyAsync(message);
            await _emailService.SendEmailAsync(
                    to: emailTo,
                    subject: subject,
                    body: message);

        }

        private static bool ShouldNotifyStripeEvent(Stripe.Event stripeEvent)
        {
            // Only notify for these
            if (stripeEvent.Type is not ("customer.subscription.created"
                or "customer.subscription.updated"
                or "customer.subscription.deleted"))
                return false;

            // Always notify on create/delete
            if (stripeEvent.Type is "customer.subscription.created" or "customer.subscription.deleted")
                return true;

            // For "updated", only notify if something meaningful changed
            var prev = stripeEvent.Data?.PreviousAttributes;
            if (prev is null || prev.Count == 0)
                return true; // if Stripe didn't include prev attrs, err on the side of notifying

            // ✅ Your “duplicate upgrade” case: only latest_invoice changed
            if (prev.Count == 1 && prev.ContainsKey("latest_invoice"))
                return false;

            // ✅ Common meaningful changes you care about
            if (prev.ContainsKey("items") ||            // quantity / price / plan changes live here
                prev.ContainsKey("quantity") ||         // sometimes present at root
                prev.ContainsKey("status") ||
                prev.ContainsKey("cancel_at_period_end") ||
                prev.ContainsKey("cancel_at") ||
                prev.ContainsKey("trial_end"))
                return true;

            // Otherwise: ignore noise (metadata, invoice pointers, etc.)
            return false;
        }

        private async Task<Client?> GetClientFromSubscriptionAsync(Subscription sub, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            if (!string.IsNullOrWhiteSpace(sub.CustomerId))
            {
                var client = await db.Clients
                    .FirstOrDefaultAsync(c => c.StripeCustomerId == sub.CustomerId);

                if (client != null)
                    return client;
            }

            // Fallback (very rare)
            if (!string.IsNullOrWhiteSpace(sub.Id))
            {
                var client = await db.Clients
                    .FirstOrDefaultAsync(c => c.StripeSubscriptionId == sub.Id);

                if (client != null)
                    return client;
            }

            return null;
        }
    }

}
