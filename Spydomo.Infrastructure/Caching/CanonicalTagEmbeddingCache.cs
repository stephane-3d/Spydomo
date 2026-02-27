using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure.Caching
{
    public class CanonicalTagEmbeddingCache
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<CanonicalTagEmbeddingCache> _logger;

        private volatile List<CachedCanonicalTag>? _cache;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public CanonicalTagEmbeddingCache(
            IDbContextFactory<SpydomoContext> dbFactory,
            ILogger<CanonicalTagEmbeddingCache> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task<List<CachedCanonicalTag>> GetCanonicalTagsAsync(CancellationToken ct = default)
        {
            var existing = _cache;
            if (existing != null)
                return existing;

            await _gate.WaitAsync(ct);
            try
            {
                existing = _cache;
                if (existing != null)
                    return existing;

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var tags = await db.CanonicalTags
                    .AsNoTracking()
                    .Where(t => t.EmbeddingJson != null && t.EmbeddingJson != "")
                    .Select(t => new { t.Id, t.Name, t.Description, t.EmbeddingJson })
                    .ToListAsync(ct);

                var list = new List<CachedCanonicalTag>(tags.Count);

                foreach (var t in tags)
                {
                    try
                    {
                        var emb = JsonSerializer.Deserialize<List<float>>(t.EmbeddingJson!);
                        if (emb == null || emb.Count == 0)
                            continue;

                        list.Add(new CachedCanonicalTag
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
                            "Failed to deserialize EmbeddingJson for CanonicalTag id={Id} name={Name}",
                            t.Id, t.Name);
                    }
                }

                _cache = list;
                _logger.LogInformation("CanonicalTagEmbeddingCache loaded {Count} tags", list.Count);
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
            _logger.LogInformation("CanonicalTagEmbeddingCache invalidated");
        }
    }


}
