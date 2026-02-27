using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Spydomo.Models;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure
{
    public sealed class SignalTypeResolver : ISignalTypeResolver
    {
        private const string CacheKey = "signaltype_slug_to_id";
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IMemoryCache _cache;

        public SignalTypeResolver(IDbContextFactory<SpydomoContext> dbFactory, IMemoryCache cache)
        {
            _dbFactory = dbFactory;
            _cache = cache;
        }

        public async Task<int> GetIdAsync(string slug, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("slug is required", nameof(slug));

            var map = await GetMapAsync(ct);

            if (!map.TryGetValue(slug, out var id))
                throw new InvalidOperationException($"SignalType slug not found in DB: '{slug}'. Did you seed it?");

            return id;
        }

        public async Task<Dictionary<string, int>> GetIdsAsync(IEnumerable<string> slugs, CancellationToken ct = default)
        {
            var wanted = slugs.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var map = await GetMapAsync(ct);

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in wanted)
            {
                if (!map.TryGetValue(s, out var id))
                    throw new InvalidOperationException($"SignalType slug not found in DB: '{s}'. Did you seed it?");
                result[s] = id;
            }
            return result;
        }

        private async Task<Dictionary<string, int>> GetMapAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(CacheKey, out Dictionary<string, int> cached))
                return cached;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var map = await db.SignalTypes
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Slug, x => x.Id, StringComparer.OrdinalIgnoreCase, ct);

            // cache for a while; seeds don’t change often
            _cache.Set(CacheKey, map, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
            });

            return map;
        }
    }
}
