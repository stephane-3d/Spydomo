using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Spydomo.DTO;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public sealed class MetaService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IMemoryCache _cache;

        public MetaService(IDbContextFactory<SpydomoContext> dbFactory, IMemoryCache cache)
        {
            _dbFactory = dbFactory;
            _cache = cache;
        }

        public async Task<List<CountryDto>> GetCountriesAsync(CancellationToken ct = default)
        {
            const string cacheKey = "meta:countries";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var countries = await db.Countries
                    .AsNoTracking()
                    .Select(c => new CountryDto { Code = c.Code, Name = c.Name })
                    .ToListAsync(ct);

                var prioritized = new[] { "CA", "US" };
                return countries
                    .OrderBy(c => !prioritized.Contains(c.Code))
                    .ThenBy(c => c.Name)
                    .ToList();
            }) ?? new List<CountryDto>();
        }

        public async Task<List<RegionDto>> GetRegionsAsync(string countryCode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
                return new List<RegionDto>();

            countryCode = countryCode.Trim().ToUpperInvariant();
            var cacheKey = $"meta:regions:{countryCode}";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                return await db.Regions
                    .AsNoTracking()
                    .Where(r => r.CountryCode == countryCode)
                    .Select(r => new RegionDto { Code = r.Code, Name = r.Name, CountryCode = r.CountryCode })
                    .OrderBy(r => r.Name ?? r.Code)
                    .ToListAsync(ct);
            }) ?? new List<RegionDto>();
        }
    }

}
