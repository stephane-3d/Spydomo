using Clerk.Net.AspNetCore.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Billing.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Web.Controllers
{
    [ApiController]
    [Route("api/subscription")]
    [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(ISubscriptionService subscriptionService, IDbContextFactory<SpydomoContext> dbFactory, ILogger<SubscriptionController> logger)
        {
            _subscriptionService = subscriptionService;
            _dbFactory = dbFactory;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<ActionResult<SubscriptionStatusDto>> GetStatus(CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user is null || user.Client == null)
                return Unauthorized();

            var status = await _subscriptionService.GetStatusAsync(user.Client);
            return status is null ? NotFound() : Ok(status);
        }


        [HttpGet("invoices")]
        public async Task<ActionResult<List<InvoiceDto>>> GetInvoices(CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user is null)
                return Unauthorized();

            if (user.Client == null)
                return NotFound("Client not found.");

            var clientEmail = user.Client.BillingEmail ?? user.Client.ContactEmail;

            var customerId = await _subscriptionService.GetStripeCustomerIdAsync(clientEmail);
            var invoices = await _subscriptionService.GetInvoicesAsync(customerId);
            return Ok(invoices);
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartSubscriptionAsync([FromQuery] int quantity, CancellationToken ct)
        {
            if (quantity <= 0)
                return BadRequest("Quantity must be greater than zero.");

            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user is null)
                return Unauthorized();

            if (user.Client == null)
                return NotFound("Client not found.");

            var client = user.Client;

            if (quantity < client.TrackedCompaniesCount)
                return BadRequest($"Quantity must be greater than your current number of tracked companies. Please select at least {client.TrackedCompaniesCount}.");

            try
            {
                var checkoutUrl = await _subscriptionService.StartSubscriptionAsync(client.Id, quantity);
                return Ok(new { url = checkoutUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate Stripe Checkout session.");
                return StatusCode(500, "Something went wrong while starting the subscription.");
            }
        }

        [HttpPost("cancel")]
        public async Task<IActionResult> CancelSubscription(CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user is null)
                return Unauthorized();

            if (user.Client == null)
                return NotFound("Client not found.");

            var client = user.Client;

            await _subscriptionService.CancelSubscriptionAsync(client.Id);

            return Ok();
        }

        [HttpPost("restart")]
        public async Task<IActionResult> RestartSubscription(CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user is null)
                return Unauthorized();

            if (user.Client == null)
                return NotFound("Client not found.");

            await _subscriptionService.RestartSubscriptionAsync(user.Client.Id);

            return Ok();
        }


        [HttpPost("update")]
        public async Task<IActionResult> UpdateSubscription([FromQuery] int quantity, CancellationToken ct)
        {
            if (quantity <= 0)
                return BadRequest("Quantity must be greater than zero.");

            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user?.Client == null)
                return Unauthorized();

            var client = user.Client;

            if (string.IsNullOrWhiteSpace(client.StripeSubscriptionId))
                return BadRequest("You do not have an active subscription.");

            if (quantity == client.PlanCompaniesCount)
                return BadRequest("You're already subscribed to this number of companies.");

            // Prevent downgrades below current usage
            if (quantity < client.PlanCompaniesCount && client.TrackedCompaniesCount > quantity)
            {
                var overage = client.TrackedCompaniesCount - quantity;
                return BadRequest($"You are currently tracking {client.TrackedCompaniesCount} companies. Please remove {overage} companies before downgrading to {quantity}.");
            }

            try
            {
                await _subscriptionService.UpdateSubscriptionQuantityAsync(client.Id, quantity);

                _logger.LogInformation($"Client {client.Id} scheduled Stripe subscription update to {quantity} companies.");

                return Ok(new { message = "Subscription update scheduled for next billing cycle." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scheduling Stripe subscription update. ClientId: {client.Id}, Quantity: {quantity}");
                return StatusCode(500, "Something went wrong while scheduling your subscription update.");
            }
        }

        [HttpPost("update-payment-method")]
        public async Task<IActionResult> UpdatePaymentMethod(CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user is null || user.Client == null)
                return Unauthorized();

            try
            {
                var url = await _subscriptionService.CreateBillingPortalSessionAsync(user.Client.Id);
                return Ok(new { url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create billing portal session. ClientId: {user.Client.Id}");
                return StatusCode(500, "Could not create billing portal session.");
            }
        }

        [HttpGet("credit")]
        public async Task<IActionResult> GetCreditAmount(CancellationToken ct)
        {
            decimal creditAmount = 0;
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            var user = await GetCurrentUserAsync(ct);
            if (user is null || user.Client == null)
                return NotFound("Client not found.");

            if (user.Client.Status != ClientStatus.TrialActive)
            {
                creditAmount = await _subscriptionService.GetCustomerCreditAsync(user.Client.Id);
            }
            return Ok(creditAmount);
        }


        private async Task<Models.User?> GetCurrentUserAsync(CancellationToken ct)
        {
            var clerkUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(clerkUserId))
                return null;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Users
                .Include(u => u.Client)
                .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);
        }

        private async Task<bool> IsCurrentUserAdminAsync(CancellationToken ct)
        {
            var user = await GetCurrentUserAsync(ct);
            var role = user?.Role?.ToLower();
            return (role == "admin");
        }
    }
}
