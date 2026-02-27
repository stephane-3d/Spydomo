using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.DTO.MarketPulse;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class MarketPulseService : IMarketPulseService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IMarketPulseGenerator _generator;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public MarketPulseService(IDbContextFactory<SpydomoContext> dbFactory, IMarketPulseGenerator generator)
        {
            _dbFactory = dbFactory;
            _generator = generator;
        }

        public async Task<MarketPulseViewModel?> GetPulseAsync(
            string slug,
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            const int timeWindowDays = 30;
            const GroupSnapshotKind kind = GroupSnapshotKind.Pulse;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var group = await db.CompanyGroups.AsNoTracking()
                .FirstOrDefaultAsync(g => g.Slug == slug && !g.IsPrivate, ct);

            if (group is null) return null;

            if (!forceRefresh)
            {
                var snap = await db.GroupSnapshots.AsNoTracking()
                    .Where(x => x.GroupId == group.Id
                             && x.Kind == kind
                             && x.TimeWindowDays == timeWindowDays)
                    .OrderByDescending(x => x.GeneratedAtUtc)
                    .FirstOrDefaultAsync(ct);

                if (snap is not null)
                {
                    var vm = JsonSerializer.Deserialize<MarketPulseViewModel>(snap.PayloadJson);
                    if (vm is not null)
                    {
                        vm.Slug = group.Slug;
                        vm.Title = $"Market Pulse: {group.Name}";
                        vm.LastUpdatedUtc = snap.GeneratedAtUtc;
                        return vm;
                    }
                }
            }

            // ✅ include kind in the single-flight key (important!)
            var key = $"{group.Id}:{kind}:{timeWindowDays}";
            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);

            try
            {
                // re-check after lock
                if (!forceRefresh)
                {
                    var snap2 = await db.GroupSnapshots.AsNoTracking()
                        .Where(x => x.GroupId == group.Id
                                 && x.Kind == kind
                                 && x.TimeWindowDays == timeWindowDays)
                        .OrderByDescending(x => x.GeneratedAtUtc)
                        .FirstOrDefaultAsync(ct);

                    if (snap2 is not null)
                    {
                        var vm2 = JsonSerializer.Deserialize<MarketPulseViewModel>(snap2.PayloadJson);
                        if (vm2 is not null)
                        {
                            vm2.Slug = group.Slug;
                            vm2.Title = $"Market Pulse: {group.Name}";
                            vm2.LastUpdatedUtc = snap2.GeneratedAtUtc;
                            return vm2;
                        }
                    }
                }

                var generatedAt = DateTime.UtcNow;
                var generated = await _generator.GenerateAsync(group, timeWindowDays, ct);

                generated.Slug = group.Slug;
                generated.Title = $"Market Pulse: {group.Name}";
                generated.LastUpdatedUtc = generatedAt;
                generated.IsPublicPreview = !group.IsPrivate;
                
                db.GroupSnapshots.Add(new GroupSnapshot
                {
                    GroupId = group.Id,
                    GroupSlug = group.Slug,
                    TimeWindowDays = timeWindowDays,
                    Kind = kind,
                    SchemaVersion = 1,
                    GeneratedAtUtc = generatedAt,
                    PayloadJson = JsonSerializer.Serialize(generated) // ✅ fixed
                });

                await db.SaveChangesAsync(ct);
                return generated;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<GroupHeaderDto?> GetGroupHeaderAsync(string slug, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.CompanyGroups.AsNoTracking()
                .Where(g => g.Slug == slug && !g.IsPrivate)
                .Select(g => new GroupHeaderDto(g.Slug, g.Name, g.IsPrivate))
                .FirstOrDefaultAsync(ct);
        }
    }

}
