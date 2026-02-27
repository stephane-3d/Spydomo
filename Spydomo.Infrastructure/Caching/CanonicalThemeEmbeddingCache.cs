using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure.Caching
{
    public class CanonicalThemeEmbeddingCache
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<CanonicalThemeEmbeddingCache> _logger;

        private volatile List<CachedCanonicalTheme>? _cache;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public CanonicalThemeEmbeddingCache(
            IDbContextFactory<SpydomoContext> dbFactory,
            ILogger<CanonicalThemeEmbeddingCache> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task<List<CachedCanonicalTheme>> GetCanonicalThemesAsync(CancellationToken ct = default)
        {
            // Fast path
            var existing = _cache;
            if (existing != null)
                return existing;

            await _gate.WaitAsync(ct);
            try
            {
                // Double-check after acquiring gate
                existing = _cache;
                if (existing != null)
                    return existing;

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var canonicalThemes = await db.CanonicalThemes
                    .AsNoTracking()
                    .Where(t => t.EmbeddingJson != null && t.EmbeddingJson != "")
                    .Select(t => new { t.Id, t.Name, t.Description, t.EmbeddingJson })
                    .ToListAsync(ct);

                var list = new List<CachedCanonicalTheme>(canonicalThemes.Count);

                foreach (var t in canonicalThemes)
                {
                    try
                    {
                        var emb = JsonSerializer.Deserialize<List<float>>(t.EmbeddingJson!);
                        if (emb == null || emb.Count == 0)
                            continue;

                        list.Add(new CachedCanonicalTheme
                        {
                            Id = t.Id,
                            Name = t.Name,
                            Description = t.Description,
                            Embedding = emb
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to deserialize EmbeddingJson for CanonicalTheme id={Id} name={Name}",
                            t.Id, t.Name);
                    }
                }

                _cache = list;
                _logger.LogInformation("CanonicalThemeEmbeddingCache loaded {Count} themes", list.Count);

                return list;
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Invalidate()
        {
            _cache = null;
            _logger.LogInformation("CanonicalThemeEmbeddingCache invalidated");
        }
    }


}
