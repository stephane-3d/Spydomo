using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.DTO;

namespace Spydomo.Infrastructure.Caching
{
    public sealed class SignalTypeOptionsProvider : ISignalTypeOptionsProvider
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SignalTypeOptionsProvider> _logger;

        private static readonly string CacheKey = "signal-types:allowed-in-llm";
        private static readonly SemaphoreSlim _lock = new(1, 1);

        // Change this if you want (30–120 min is typical here)
        private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

        public SignalTypeOptionsProvider(
            IDbContextFactory<SpydomoContext> dbFactory,
            IMemoryCache cache,
            ILogger<SignalTypeOptionsProvider> logger)
        {
            _dbFactory = dbFactory;
            _cache = cache;
            _logger = logger;
        }

        public void Invalidate() => _cache.Remove(CacheKey);

        public async Task<List<SignalTypeOption>> GetAllowedAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!forceRefresh && _cache.TryGetValue(CacheKey, out List<SignalTypeOption> cached))
                return cached;

            await _lock.WaitAsync(ct);
            try
            {
                // Double-check after acquiring lock
                if (!forceRefresh && _cache.TryGetValue(CacheKey, out cached))
                    return cached;

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var allowed = await db.SignalTypes
                    .AsNoTracking()
                    .Where(x => x.AllowedInLlm)
                    .OrderBy(x => x.Id)
                    .Select(x => new SignalTypeOption(x.Id, x.Name, x.Description))
                    .ToListAsync(ct);

                // Cache options
                var entryOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Ttl
                };

                _cache.Set(CacheKey, allowed, entryOptions);

                _logger.LogInformation("Cached {Count} SignalTypes (AllowedInLlm=true) for {Ttl}.", allowed.Count, Ttl);
                return allowed;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
