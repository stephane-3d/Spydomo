using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Spydomo.DTO;
using Spydomo.Infrastructure.Clerk;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Spydomo.Infrastructure
{
    public class ClientContextService : IClientContextService
    {
        private readonly HttpClient _http;
        private readonly ClerkJsInterop _clerkJs;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        private WhoAmIResponse? _cached;
        private readonly SemaphoreSlim _loadGate = new(1, 1);

        public ClientContextService(
            HttpClient http,
            ClerkJsInterop clerkJs,
            IDbContextFactory<SpydomoContext> dbFactory)
        {
            _http = http;
            _clerkJs = clerkJs;
            _dbFactory = dbFactory;
        }

        public async Task<int> GetCurrentClientIdAsync()
        {
            await EnsureLoadedAsync();
            return _cached?.ClientId ?? throw new Exception("Client not found.");
        }

        public async Task<int> GetCurrentUserIdAsync()
        {
            await EnsureLoadedAsync();
            return _cached?.Id ?? throw new Exception("User not found.");
        }

        private async Task EnsureLoadedAsync()
        {
            if (_cached != null) return;

            await _loadGate.WaitAsync();
            try
            {
                if (_cached != null) return;
                await LoadAsync();
            }
            finally
            {
                _loadGate.Release();
            }
        }

        private async Task LoadAsync()
        {
            string? token;
            try
            {
                token = await _clerkJs.GetSessionTokenAsync();
            }
            catch (JSDisconnectedException)
            {
                // Circuit died; leave _cached null and let callers decide.
                return;
            }

            if (string.IsNullOrWhiteSpace(token))
                return;

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/whoami");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return;

            _cached = await response.Content.ReadFromJsonAsync<WhoAmIResponse>();
        }

        public async Task<CompanyQuotaStatus> GetCompanyQuotaStatusAsync()
        {
            await EnsureLoadedAsync();

            var clientId = _cached?.ClientId ?? throw new Exception("Client not found.");

            await using var db = await _dbFactory.CreateDbContextAsync();

            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
            if (client == null) throw new Exception("Client not found in database.");

            return new CompanyQuotaStatus
            {
                TotalAllowed = client.PlanCompaniesCount,
                CurrentlyTracked = client.TrackedCompaniesCount
            };
        }
    }
}
