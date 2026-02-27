using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public sealed class CurrentUserState : ICurrentUserState
    {
        private readonly AuthenticationStateProvider _auth;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        private UserDto? _current;
        private DateTimeOffset _expiresAt;

        public CurrentUserState(AuthenticationStateProvider auth, IDbContextFactory<SpydomoContext> dbFactory)
        {
            _auth = auth;
            _dbFactory = dbFactory;
        }

        public async Task<UserDto?> GetOrLoadAsync(CancellationToken ct = default)
        {
            if (_current is not null && _expiresAt > DateTimeOffset.UtcNow)
                return _current;

            var state = await _auth.GetAuthenticationStateAsync();
            var clerkUserId = state.User.FindFirst("sub")?.Value;

            if (string.IsNullOrWhiteSpace(clerkUserId))
            {
                Clear();
                return null;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            _current = await db.Users.AsNoTracking()
                .Where(u => u.ClerkUserId == clerkUserId)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Email = u.Email,
                    Role = u.Role,
                    ClientId = u.ClientId ?? 0
                })
                .FirstOrDefaultAsync(ct);

            if (_current is null)
            {
                Clear();
                return null;
            }

            _expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            return _current;
        }

        public async Task<int?> GetClientIdAsync(CancellationToken ct = default)
            => (await GetOrLoadAsync(ct))?.ClientId;

        public bool IsInRole(string role)
            => string.Equals(_current?.Role, role, StringComparison.OrdinalIgnoreCase);

        public void Clear()
        {
            _current = null;
            _expiresAt = default;
        }
    }
}
