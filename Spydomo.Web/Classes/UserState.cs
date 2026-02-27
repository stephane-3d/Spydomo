using Spydomo.DTO;
using Spydomo.Infrastructure.Clerk;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Spydomo.Web.Classes
{
    public sealed class UserState
    {
        private readonly HttpClient _http;
        private readonly IBrowserStorage _storage;
        private readonly ClerkJsInterop _clerkJs; // your existing interop to get session token

        private const string CacheKey = "spydomo:user";
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        // in-memory (per circuit) cache
        private UserDto? _current;
        private DateTimeOffset _expiresAt;

        public bool IsLoaded => _current is not null && _expiresAt > DateTimeOffset.UtcNow;
        public UserDto? Current => _current;

        private sealed record CachedUser(UserDto User, DateTimeOffset ExpiresAt);

        public UserState(HttpClient http, IBrowserStorage storage, ClerkJsInterop clerkJs)
        { _http = http; _storage = storage; _clerkJs = clerkJs; }

        public async Task<UserDto?> GetOrLoadAsync(CancellationToken ct = default)
        {
            if (IsLoaded) return _current;

            // 1) Try sessionStorage (JS: only call this in/after OnAfterRenderAsync)
            var cached = await _storage.GetSessionAsync<CachedUser>(CacheKey);
            if (cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                _current = cached.User; _expiresAt = cached.ExpiresAt;
                return _current;
            }

            // 2) Load from server using Clerk token
            var token = await _clerkJs.GetSessionTokenAsync();

            if (string.IsNullOrWhiteSpace(token))
            {
                // No active session – return null so caller can handle as “not logged in”
                return null;
            }

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/users/whoami");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _http.SendAsync(req, ct);

            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // user not created yet (first login) — treat as “not ready”
                await ClearAsync();
                return null;
            }

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await ClearAsync();
                return null;
            }

            res.EnsureSuccessStatusCode();

            _current = await res.Content.ReadFromJsonAsync<UserDto>(JsonOpts);
            _expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

            await _storage.SetSessionAsync(CacheKey, new CachedUser(_current!, _expiresAt));
            return _current;
        }

        public async Task ClearAsync()
        {
            _current = null; _expiresAt = default;
            await _storage.RemoveSessionAsync(CacheKey);
        }

        public bool IsInRole(string role) =>
            string.Equals(_current?.Role, role, StringComparison.OrdinalIgnoreCase);
    }
}
