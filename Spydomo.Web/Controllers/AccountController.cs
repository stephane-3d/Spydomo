using Clerk.Net.AspNetCore.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Clerk;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Stripe;

namespace Spydomo.Web.Controllers
{
    [ApiController]
    [Route("api/account")]
    public class AccountController : ControllerBase
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<SubscriptionController> _logger;
        private readonly ClerkBackend _clerkBackend;

        public AccountController(IDbContextFactory<SpydomoContext> dbFactory, ISubscriptionService subscriptionService, ILogger<SubscriptionController> logger, ClerkBackend clerkBackend)
        {
            _dbFactory = dbFactory;
            _subscriptionService = subscriptionService;
            _logger = logger;
            _clerkBackend = clerkBackend;
        }

        [HttpGet("client")]
        [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetClient(CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync())
                return Forbid();

            var clerkUserId = User.FindFirst("sub")?.Value;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var user = await db.Users.Include(u => u.Client)
                                      .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            if (user?.Client == null)
                return NotFound();

            var client = user.Client;

            var dto = new ClientDto
            {
                Name = client.Name,
                CountryCode = client.CountryCode,
                RegionCode = client.RegionCode,
                City = client.City,
                ContactName = client.ContactName,
                ContactEmail = client.ContactEmail,
                BillingEmail = client.BillingEmail,
                AddressLine1 = client.AddressLine1,
                PostalCode = client.PostalCode
            };

            return Ok(dto);
        }

        [HttpPut("client")]
        [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateClient([FromBody] ClientDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!await IsCurrentUserAdminAsync())
                return Forbid();

            var clerkUserId = User.FindFirst("sub")?.Value;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var user = await db.Users.Include(u => u.Client)
                                      .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            if (user?.Client == null)
                return NotFound();

            var client = user.Client;

            client.Name = dto.Name;
            client.CountryCode = dto.CountryCode;
            client.RegionCode = dto.RegionCode;
            client.City = dto.City;
            client.ContactName = dto.ContactName;
            client.ContactEmail = dto.ContactEmail;
            client.BillingEmail = dto.BillingEmail;
            client.AddressLine1 = dto.AddressLine1;
            client.PostalCode = dto.PostalCode;

            // ✅ Sync to Stripe if applicable
            if (!string.IsNullOrWhiteSpace(client.StripeCustomerId))
            {
                var customerService = new CustomerService();
                await customerService.UpdateAsync(client.StripeCustomerId, new CustomerUpdateOptions
                {
                    Address = new AddressOptions
                    {
                        Line1 = client.AddressLine1 ?? "N/A",
                        City = client.City,
                        State = client.RegionCode,
                        PostalCode = client.PostalCode,
                        Country = client.CountryCode
                    },
                    Name = client.Name,
                    Email = client.BillingEmail
                });
            }

            await db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("delete")]
        [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> DeleteAccount(CancellationToken ct)
        {
            // Only account admins can delete the whole account
            if (!await IsCurrentUserAdminAsync())
                return Forbid();

            var clerkUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(clerkUserId))
                return Unauthorized();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Load current user + their client + all client users
            var user = await db.Users
                .Include(u => u.Client)
                    .ThenInclude(c => c.Users)
                .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            if (user is null)
                return Unauthorized();

            if (user.Client is null)
                return NotFound("Client not found.");

            var client = user.Client;

            // 1) Cancel Stripe subscription (if any)
            try
            {
                if (!string.IsNullOrWhiteSpace(client.StripeSubscriptionId))
                {
                    await _subscriptionService.CancelSubscriptionAsync(client.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling subscription for client {ClientId}", client.Id);
                // Optional: return 500 or just log & keep going
                // return StatusCode(StatusCodes.Status500InternalServerError, "Error cancelling subscription.");
            }

            // 2) Deactivate client + users, queue cleanup
            client.Status = ClientStatus.Deleted;
            client.DeletionRequestedAt = DateTime.UtcNow;

            foreach (var u in client.Users)
            {
                u.IsActive = false;
            }

            await db.SaveChangesAsync();

            // 3) Delete/ revoke users in Clerk (current + teammates)
            try
            {
                // If your User entity has ClerkUserId for each user, you can delete them all:
                foreach (var u in client.Users.Where(u => !string.IsNullOrWhiteSpace(u.ClerkUserId)))
                {
                    try
                    {
                        await _clerkBackend.DeleteUserAsync(u.ClerkUserId);
                    }
                    catch (Exception exUser)
                    {
                        _logger.LogError(exUser, "Error deleting Clerk user {ClerkUserId}", u.ClerkUserId);
                        // don't fail the whole operation because of one Clerk user
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Clerk users for client {ClientId}", client.Id);
                // same: log, but don't necessarily fail the entire request
            }

            // 4) (Optional) enqueue deeper cleanup job if you have a job service
            // await _accountCleanupService.QueueCleanupAsync(client.Id);

            return Ok();
        }

        private async Task<bool> IsCurrentUserAdminAsync(CancellationToken ct = default)
        {
            var clerkUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(clerkUserId))
                return false;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);
            var role = user?.Role?.ToLower();
            return (role == "admin");
        }
    }
}
