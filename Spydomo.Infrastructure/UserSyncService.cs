using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public class UserSyncService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<UserSyncService> _logger;
        private readonly IMemoryCache _cache;
        private readonly ISlackNotifier _slack;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;

        public UserSyncService(IDbContextFactory<SpydomoContext> dbFactory, ILogger<UserSyncService> logger, IMemoryCache cache,
            ISlackNotifier slack, IEmailService emailService, IConfiguration config)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _cache = cache;
            _slack = slack;
            _emailService = emailService;
            _config = config;
        }

        public async Task<User> SyncClerkUserAsync(string clerkUserId, string email, string? fullName = null, long? createdAtUnix = null, CancellationToken ct = default)
        {
            var cacheKey = $"user-sync-{clerkUserId}";
            if (_cache.TryGetValue<User>(cacheKey, out var cachedUser))
            {
                return cachedUser;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var user = await db.Users.Include(u => u.Client)
                .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            if (user == null)
            {
                // First check if it's an invitation
                var existingInvitation = await db.Users
                    .FirstOrDefaultAsync(u => u.Email == email && u.InvitationStatus == "sent");

                if (existingInvitation != null)
                {
                    existingInvitation.InvitationStatus = "accepted";
                    existingInvitation.ClerkUserId = clerkUserId;
                    existingInvitation.LastVisit = DateTime.UtcNow;
                    existingInvitation.VisitsCount = 1;

                    _logger.LogInformation("Accepted invitation from Clerk: {Email}", email);

                    await db.SaveChangesAsync();

                    _cache.Set(cacheKey, existingInvitation, TimeSpan.FromMinutes(5));

                    return existingInvitation;
                }
                else
                {
                    Client? createdClient = null;
                    User? createdUser = null;

                    // Auto-create a client
                    var strategy = db.Database.CreateExecutionStrategy();

                    await strategy.ExecuteAsync(async () =>
                    {
                        await using var tx = await db.Database.BeginTransactionAsync(ct);

                        var client = new Client
                        {
                            Name = fullName ?? email.Split('@')[0],
                            ContactName = fullName,
                            ContactEmail = email,
                            BillingEmail = email,
                            DateCreated = DateTime.UtcNow,
                            IsTrial = true,
                            TrialEndsAt = DateTime.UtcNow.AddDays(14),
                            Status = ClientStatus.TrialActive
                        };

                        db.Clients.Add(client);
                        await db.SaveChangesAsync(ct); // client.Id becomes available

                        var defaultGroup = new CompanyGroup
                        {
                            ClientId = client.Id,
                            Name = "Default group",
                            Description = "Start here — add companies and Spydomo will begin detecting signals.",
                            Slug = $"default-{client.Id}",
                            Context = null,
                            IsPrivate = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.CompanyGroups.Add(defaultGroup);

                        user = new User
                        {
                            ClerkUserId = clerkUserId,
                            Email = email,
                            Name = fullName,
                            IsActive = true,
                            DateCreated = createdAtUnix.HasValue
                                ? DateTimeOffset.FromUnixTimeMilliseconds(createdAtUnix.Value).UtcDateTime
                                : DateTime.UtcNow,
                            LastVisit = DateTime.UtcNow,
                            VisitsCount = 1,
                            Role = "admin",
                            ClientId = client.Id
                        };

                        db.Users.Add(user);
                        _logger.LogInformation("Created new user and client from Clerk: {Email}", email);

                        await db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);

                        // capture for post-commit notifications
                        createdClient = client;
                        createdUser = user;
                    });

                    // send notifications
                    if (createdClient != null && createdUser != null)
                    {
                        await SafeExecuteAsync("notify-new-client", () => NotifyNewClientCreatedAsync(createdClient, createdUser, ct));
                        await SafeExecuteAsync("welcome-email", () => SendWelcomeEmailAsync(createdClient, createdUser, ct));
                    }
                }
            }
            else
            {
                if (user.LastVisit != null && user.LastVisit < DateTime.UtcNow.AddHours(-24))
                    user.VisitsCount++;

                user.LastVisit = DateTime.UtcNow;
                user.Email = email ?? user.Email;
                user.Name = user.Name;

                _logger.LogInformation("Updated existing user visit time: {Email}", email);
            }

            _cache.Set(cacheKey, user, TimeSpan.FromMinutes(5));
            return user;
        }

        private async Task SafeExecuteAsync(string name, Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Post-signup notification failed: {Name}", name);
            }
        }


        private async Task NotifyNewClientCreatedAsync(Client client, User user, CancellationToken ct)
        {
            var subject = "🎉 New User Created";

            var message =
                $"New user signup\n" +
                $"Client Id: {client.Id}\n" +
                $"User: {user.Email}\n" +
                $"Trial: {(client.IsTrial ? "yes" : "no")} until {client.TrialEndsAt:yyyy-MM-dd}\n" +
                $"Created: {client.DateCreated:yyyy-MM-dd HH:mm} UTC\n";

            await _slack.NotifyAsync(message);

            var emailTo = _config["AcsEmail:AdminNotifyTo"]!; // your internal inbox
            await _emailService.SendEmailAsync(
                to: emailTo,
                subject: subject,
                body: message,
                ct: ct
            );
        }

        private async Task SendWelcomeEmailAsync(Client client, User user, CancellationToken ct)
        {
            var to = user.Email;
            var subject = "Welcome to Spydomo — quick question";

            var name = user.Name ?? user.Email.Split('@')[0];

            // If you have a config for your app URL:
            var appUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://spydomo.com";
            var companiesUrl = $"{appUrl}/app/companies"; // adjust to your real route

            var body =
        $@"Hi there,

Welcome to Spydomo 👋

Quick question: what made you try Spydomo today? What problem are you hoping it solves?

Just hit reply with 1–2 sentences. I read every reply.

To get started: add 2–3 companies to track, and Spydomo will start surfacing signals automatically.

Start here: {companiesUrl}

- Stephane, founder
https://spydomo.com";

            await _emailService.SendEmailAsync(
                to: to,
                subject: subject,
                body: body,
                replyTo: "stephane@spydomo.com",
                replyToDisplayName: "Stephane"
            );
        }


    }

}
