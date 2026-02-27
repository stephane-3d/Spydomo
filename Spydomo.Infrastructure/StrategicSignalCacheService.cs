using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure
{
    public class StrategicSignalCacheService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<StrategicSignalCacheService> _logger;

        public StrategicSignalCacheService(IDbContextFactory<SpydomoContext> dbFactory, ILogger<StrategicSignalCacheService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task<List<StrategicSignal>> GetOrCreateGlobalCacheAsync(
            string source,
            Func<Task<List<StrategicSignal>>> generator,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var cacheThreshold = now.AddMinutes(-30);
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var recent = await db.StrategicSignalCache
                .Where(c => c.Source == source && c.GroupId == null && c.GeneratedOn >= cacheThreshold)
                .Select(c => c.ContentJson)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrEmpty(recent))
            {
                try
                {
                    var signals = JsonSerializer.Deserialize<List<StrategicSignal>>(recent);
                    if (signals != null)
                        return signals;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to deserialize global StrategicSignal cache for source {Source}", source);
                }
            }

            var fresh = await generator();

            db.StrategicSignalCache.Add(new StrategicSignalCache
            {
                Source = source,
                GroupId = null,
                GeneratedOn = now,
                ContentJson = JsonSerializer.Serialize(fresh)
            });

            await db.SaveChangesAsync();
            return fresh;
        }


        public async Task<List<StrategicSignal>> GetOrCreateGroupLevelCacheAsync(
            int groupId,
            string source,
            Func<Task<List<StrategicSignal>>> generateFunc,
            int freshnessDays = 15,
            CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow.AddDays(-freshnessDays);
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var cached = await db.StrategicSignalCache
                .FirstOrDefaultAsync(c =>
                    c.GroupId == groupId &&
                    c.Source == source &&
                    c.GeneratedOn >= cutoff);

            if (cached != null)
            {
                return JsonSerializer.Deserialize<List<StrategicSignal>>(cached.ContentJson) ?? new();
            }

            var fresh = await generateFunc();

            var newCache = new StrategicSignalCache
            {
                GroupId = groupId,
                Source = source,
                ContentJson = JsonSerializer.Serialize(fresh),
                GeneratedOn = DateTime.UtcNow
            };

            db.StrategicSignalCache.Add(newCache);
            await db.SaveChangesAsync();

            return fresh;
        }

    }

}
