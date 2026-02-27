using Clerk.BackendAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spydomo.Models;
using System.Security.Claims;

namespace Spydomo.Infrastructure.Clerk
{
    public class ClerkUserSyncMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ClerkUserSyncMiddleware> _logger;

        public ClerkUserSyncMiddleware(RequestDelegate next, ILogger<ClerkUserSyncMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ClerkBackendApi clerkApi, UserSyncService userSyncService)
        {
            var path = context.Request.Path.Value ?? "";

            // ✅ EARLY BYPASS: auth/static/infra should never sync
            if (path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/app/login", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/app/login/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/app/signup", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/app/signup/", StringComparison.OrdinalIgnoreCase) ||
                IsInfraPath(path) ||
                IsStatic(path))
            {
                await _next(context);
                return;
            }

            // ✅ Global bypass (ops toggle): set DISABLE_CLERK_SYNC=1 in App Settings
            if (Environment.GetEnvironmentVariable("DISABLE_CLERK_SYNC") == "1")
            {
                await _next(context);
                return;
            }

            // ✅ Ensure user exists BEFORE endpoints run (prevents whoami 404 race)
            try
            {
                var ct = context.RequestAborted;

                var user = context.User;
                if (user?.Identity?.IsAuthenticated == true &&
                    !context.Items.ContainsKey("__clerk_synced"))
                {
                    var clerkUserId = GetClerkUserId(user);
                    if (!string.IsNullOrWhiteSpace(clerkUserId))
                    {
                        var db = context.RequestServices.GetRequiredService<SpydomoContext>();
                        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
                        var existsKey = $"clerk-exists:{clerkUserId}";
                        var cooldownKey = $"clerk-sync-cooldown:{clerkUserId}";

                        // 1) exists cache
                        if (!cache.TryGetValue(existsKey, out bool exists))
                        {
                            exists = await db.Users.AsNoTracking()
                                .AnyAsync(u => u.ClerkUserId == clerkUserId, context.RequestAborted);

                            cache.Set(existsKey, exists, exists ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(2));
                        }

                        // Mark attempted for this request so we don't re-enter later in pipeline
                        context.Items["__clerk_synced"] = true;

                        if (!exists)
                        {
                            // If we've tried recently, skip Clerk call but continue request pipeline
                            if (!cache.TryGetValue(cooldownKey, out _))
                            {
                                cache.Set(cooldownKey, true, TimeSpan.FromMinutes(2));

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

                                        cache.Set(existsKey, true, TimeSpan.FromMinutes(60));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    cache.Set(cooldownKey, true, TimeSpan.FromMinutes(10));
                                    _logger.LogWarning(ex, "Clerk Users.GetAsync failed for {ClerkUserId}", clerkUserId);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Never break the request pipeline because sync failed
                _logger.LogError(ex, "Pre-next Clerk sync failed.");
            }

            await _next(context);

            // Post-next: do nothing (avoid redirect loops / response started issues)
        }

        private static string? GetClerkUserId(ClaimsPrincipal user)
        {
            // Preferred:
            var id = user.FindFirst("user_id")?.Value
                     ?? user.FindFirst("https://schemas.clerk.com/user_id")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("sub")?.Value; // last resort
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }


        static bool IsStatic(string? path) =>
        path is not null && (
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||   // ✅ add
            path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||    // optional
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||    // optional
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||   // optional
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||    // optional
            path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||   // optional
            path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||  // optional
            path.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ||    // optional
            path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
        );

        static bool IsInfraPath(string? p) =>
        p is not null && (
            p.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ||      // ✅ add
            p.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase) ||  // ✅ optional but recommended
            p.StartsWith("/clerk", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/sign-in", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/sign-up", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/verify", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/sso-callback", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/invitations", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/api/clerk", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
        );
    }
}
