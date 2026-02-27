using Clerk.BackendAPI;
using Clerk.Net.AspNetCore.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydomo.DTO;
using Spydomo.Infrastructure;
using Spydomo.Infrastructure.Clerk;
using Spydomo.Models;
using System.Security.Claims;
using System.Text.Json;

namespace Spydomo.Web.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UserController : ControllerBase
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<UserController> _logger;
        private readonly ClerkBackend _clerkBackend;

        public sealed record UpdateUserRoleDto(string Role);

        public UserController(IDbContextFactory<SpydomoContext> dbFactory, ILogger<UserController> logger, ClerkBackend clerkBackend)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _clerkBackend = clerkBackend;
        }

        [HttpGet("whoami")]
        [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> WhoAmI(
            [FromServices] ClerkBackendApi clerkApi,
            [FromServices] UserSyncService userSyncService,
            CancellationToken ct)
        {
            var clerkUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(clerkUserId))
                return Unauthorized();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            async Task<User?> LoadUserAsync() =>
                await db.Users
                    .Include(u => u.Client)
                    .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            var user = await LoadUserAsync();

            if (user == null)
            {
                // ✅ Self-heal: first login race (or missed middleware) — sync once then retry
                try
                {
                    var result = await clerkApi.Users.GetAsync(clerkUserId);
                    if (result?.User is { } clerkUser)
                    {
                        await userSyncService.SyncClerkUserAsync(
                            clerkUserId: clerkUser.Id,
                            email: clerkUser.EmailAddresses.FirstOrDefault()?.EmailAddressValue,
                            fullName: $"{clerkUser.FirstName} {clerkUser.LastName}".Trim(),
                            createdAtUnix: clerkUser.CreatedAt
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WhoAmI sync attempt failed for ClerkUserId={ClerkUserId}", clerkUserId);
                }

                user = await LoadUserAsync();
            }

            if (user == null)
                return NotFound("User not found in DB.");

            return Ok(new
            {
                user.Id,
                user.ClerkUserId,
                user.Name,
                user.Email,
                user.Role,
                user.IsActive,
                user.DateCreated,
                user.LastVisit,
                user.ClientId,
                user.InvitationStatus
            });
        }

        // POST /api/user/update-name
        [Authorize]
        [HttpPost("update-name")]
        public async Task<IActionResult> UpdateUserName([FromBody] UpdateUserNameRequest request, CancellationToken ct)
        {
            var clerkUserId = User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(clerkUserId))
                return Unauthorized();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);
            if (user == null)
                return NotFound();

            user.Name = request.Name?.Trim();
            await db.SaveChangesAsync();

            _logger.LogInformation("Updated user name for ClerkUserId {ClerkUserId}", clerkUserId);

            return Ok();
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetUsersForClient(CancellationToken ct)
        {
            var clerkUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(clerkUserId))
                return Unauthorized();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var currentUser = await db.Users
                .Include(u => u.Client)
                .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            if (currentUser == null)
                return NotFound("User not found");

            if (currentUser.Role != "admin")
                return Forbid("Only admins can view user list");

            var users = await db.Users
                .Where(u => u.ClientId == currentUser.ClientId)
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.Email,
                    u.Role,
                    u.VisitsCount,
                    u.DateCreated,
                    u.InvitationStatus
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpPost("invite")]
        [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request, CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var clerkUserId = User.FindFirst("sub")?.Value;
            var admin = await db.Users.Include(u => u.Client)
                                        .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            if (admin is null) return Unauthorized();

            var senderName = GetNiceSenderName(admin.Name, admin.Email);
            var senderEmail = admin.Email; // or admin.Client?.BillingEmail, etc.

            var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email && u.ClientId == admin.ClientId);
            if (existingUser != null)
                return Conflict("User with this email already exists.");

            try
            {
                // 🟢 Step 1: Send the Clerk invitation and get the ClerkUserId
                var newClerkUserId = await _clerkBackend.InviteUserAsync(
                    inviteeEmail: request.Email,
                    inviteeFullName: request.Name,
                    senderName: senderName,
                    senderEmail: senderEmail
                );

                // 🟢 Step 2: Add the user to your DB
                var newUser = new User
                {
                    Name = request.Name,
                    ClerkUserId = newClerkUserId,
                    Email = request.Email,
                    Role = request.Role,
                    ClientId = admin.ClientId,
                    InvitationStatus = "sent",
                    DateCreated = DateTime.UtcNow,
                    IsActive = true
                };

                db.Users.Add(newUser);
                await db.SaveChangesAsync();

                return Ok(new { message = "User invited successfully." });
            }
            catch (Exception ex)
            {
                var longMessage = ExtractLongMessage(ex.Message);

                _logger.LogError(ex, $"Failed to invite user via Clerk. ClientId: {admin.ClientId} Email: {request.Email} Message: {ex.Message} Long message: {longMessage}");

                if (string.IsNullOrEmpty(longMessage))
                    longMessage = "Failed to send invitation.";

                return StatusCode(500, longMessage);
            }
        }


        [HttpDelete("{id}")]
        [Authorize(AuthenticationSchemes = ClerkAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> DeleteUserAsync(int id, CancellationToken ct)
        {
            // Must be admin to manage users
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var user = await db.Users.FindAsync(id);
            if (user is null)
                return NotFound("User not found.");

            // Prevent deleting the last remaining admin
            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                var adminCount = await db.Users.CountAsync(u => u.Role == "admin");
                if (adminCount <= 1)
                    return Conflict("Cannot delete the last admin.");
            }

            // (Optional) Prevent deleting yourself — uncomment if you want that rule
            var currentUserId = await GetCurrentUserIdAsync(ct); // implement based on your auth
            if (user.Id == currentUserId)
                return Conflict("You cannot delete your own account.");

            // If linked, delete from Clerk first; if it fails, abort the DB delete
            if (!string.IsNullOrEmpty(user.ClerkUserId))
            {
                try
                {
                    await _clerkBackend.DeleteUserAsync(user.ClerkUserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to delete user from Clerk. ClientId: {user.ClientId} Email: {user.Email} Message: {ex.Message}");
                    return StatusCode(500, "Failed to delete user from Clerk.");
                }
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("/api/users/{id:int}/role")]
        public async Task<IActionResult> UpdateUserRoleAsync(int id, [FromBody] UpdateUserRoleDto body, CancellationToken ct)
        {
            if (!await IsCurrentUserAdminAsync(ct))
                return Forbid();

            if (body is null || string.IsNullOrWhiteSpace(body.Role))
                return BadRequest("Missing role.");

            var normalizedRole = body.Role.Trim().ToLowerInvariant();
            if (normalizedRole is not ("admin" or "user"))
                return BadRequest("Invalid role. Allowed values: 'admin' or 'user'.");

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var user = await db.Users.FindAsync(id);
            if (user is null)
                return NotFound("User not found.");

            // No-op: already that role
            if (string.Equals(user.Role, normalizedRole, StringComparison.OrdinalIgnoreCase))
                return NoContent();

            // Prevent removing the last admin
            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedRole, "admin", StringComparison.OrdinalIgnoreCase))
            {
                var adminCount = await db.Users.CountAsync(u => u.Role == "admin");
                if (adminCount <= 1)
                    return Conflict("Cannot remove the last admin.");
            }

            // (Optional) Prevent demoting yourself if you’re the last admin
            var currentUserId = await GetCurrentUserIdAsync(ct);
            if (user.Id == currentUserId && normalizedRole != "admin")
            {
                var adminCount = await db.Users.CountAsync(u => u.Role == "admin");
                if (adminCount <= 1)
                    return Conflict("You cannot demote yourself if you are the last admin.");
            }

            user.Role = normalizedRole;
            await db.SaveChangesAsync();

            return NoContent();
        }
        private async Task<bool> IsCurrentUserAdminAsync(CancellationToken ct)
        {
            var clerkUserId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(clerkUserId))
                return false;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);
            var role = user?.Role?.ToLower();
            return (role == "admin");
        }

        private async Task<int?> GetCurrentUserIdAsync(CancellationToken ct)
        {
            // Clerk puts the user id in the "sub" claim. Fallback to NameIdentifier just in case.
            var clerkUserId = User.FindFirst("sub")?.Value
                             ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(clerkUserId))
                return null;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Project to the Id to avoid materializing the whole entity
            return await db.Users
                .AsNoTracking()
                .Where(u => u.ClerkUserId == clerkUserId)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync();
        }

        private string? ExtractLongMessage(string errorString)
        {
            const string prefix = "Clerk invitation failed: ";

            string jsonPart = errorString.StartsWith(prefix)
                ? errorString.Substring(prefix.Length)
                : errorString;

            try
            {
                using var doc = JsonDocument.Parse(jsonPart);
                var errors = doc.RootElement.GetProperty("errors");

                if (errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    return errors[0].GetProperty("long_message").GetString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Clerk error response: {ex.Message}");
            }

            return null;
        }

        static string GetNiceSenderName(string? name, string? email)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            if (string.IsNullOrWhiteSpace(email))
                return "Spydomo";

            var local = email.Split('@')[0];              // "stephane.guerin"
            local = local.Replace('.', ' ')
                         .Replace('_', ' ')
                         .Replace('-', ' ')
                         .Trim();

            if (string.IsNullOrWhiteSpace(local))
                return email;

            // Title-case-ish
            var parts = local.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nice = string.Join(" ", parts.Select(p =>
                p.Length == 1 ? p.ToUpperInvariant()
                              : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

            return nice;
        }

        public class UpdateUserNameRequest
        {
            public string Name { get; set; } = string.Empty;
        }
    }
}
