using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.AiServices;
using Spydomo.Infrastructure.Caching;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Text.Json;

namespace Spydomo.Infrastructure
{
    public class TagNormalizer : ITagNormalizer
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly OpenAiEmbeddingService _embeddingService;
        private readonly CanonicalTagEmbeddingCache _embeddingCache;
        private readonly ISlugService _slugService;
        private readonly ILogger<TagNormalizer> _logger;

        public TagNormalizer(
            IDbContextFactory<SpydomoContext> dbFactory,
            OpenAiEmbeddingService embeddingService,
            CanonicalTagEmbeddingCache embeddingCache,
            ISlugService slugService,
            ILogger<TagNormalizer> logger)
        {
            _dbFactory = dbFactory;
            _embeddingService = embeddingService;
            _embeddingCache = embeddingCache;
            _slugService = slugService;
            _logger = logger;
        }

        public async Task<TagNormalizerResult> NormalizeAsync(string rawTag, string reason, int? companyId = null, CancellationToken ct = default)
        {
            const double MinScore = 0.90;
            const double MinMargin = 0.015;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            if (string.IsNullOrWhiteSpace(rawTag))
                throw new ArgumentException("rawTag is required", nameof(rawTag));

            var cleaned = rawTag.Trim().ToLowerInvariant();

            // Extract sentiment symbol
            var sentiment = cleaned.EndsWith("+") ? "+" :
                            cleaned.EndsWith("-") ? "-" : "";

            if (!string.IsNullOrEmpty(sentiment))
                cleaned = cleaned[..^1].Trim(); // Remove last char + trim again

            // 1) Exact match
            var canonical = await db.CanonicalTags.FirstOrDefaultAsync(t => t.Name == cleaned);
            if (canonical != null)
            {
                return new TagNormalizerResult
                {
                    CanonicalTag = canonical,
                    RawTag = rawTag,
                    Sentiment = sentiment,
                    ConfidenceScore = 1.0,
                    IsNewCanonical = false
                };
            }

            // 2) Embedding match
            var embeddingInput = BuildTagEmbeddingText(cleaned, reason);
            var rawEmbedding = await _embeddingService.GetEmbeddingAsync(embeddingInput, companyId);

            var canonicals = await _embeddingCache.GetCanonicalTagsAsync();

            if (canonicals.Count > 0)
            {
                var candidates = canonicals
                    .Select(c => (Item: c, Embedding: (IReadOnlyList<float>)c.Embedding))
                    .ToList();

                var (bestItem, bestScore, secondItem, secondScore) =
                    SimilarityHelper.FindTop2EmbeddingMatch(rawEmbedding, candidates);

                _logger.LogDebug(
                    "TagNormalize raw='{Raw}' cleaned='{Cleaned}' best={BestName} score={BestScore:F3} second={SecondName} second={SecondScore:F3}",
                    rawTag, cleaned,
                    bestItem?.Name, bestScore,
                    secondItem?.Name, secondScore);

                if (bestItem != null && bestScore >= MinScore && (bestScore - secondScore) >= MinMargin)
                {
                    var matched = await db.CanonicalTags.FindAsync(bestItem.Id);

                    if (matched != null)
                    {
                        return new TagNormalizerResult
                        {
                            CanonicalTag = matched,
                            RawTag = rawTag,
                            Sentiment = sentiment,
                            ConfidenceScore = bestScore,
                            IsNewCanonical = false
                        };
                    }
                }
            }

            // 3) No good match => create new canonical
            var newCanonical = new CanonicalTag
            {
                Name = cleaned,
                Description = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                EmbeddingJson = JsonSerializer.Serialize(rawEmbedding),
                CreatedAt = DateTime.UtcNow,
                Slug = await _slugService.GenerateUniqueSlugAsync(cleaned, EntityType.Tag) // ✅ Tag (not Theme)
            };

            db.CanonicalTags.Add(newCanonical);
            await db.SaveChangesAsync();

            _embeddingCache.Invalidate();

            _logger.LogInformation("Created new CanonicalTag name='{Name}' id={Id}", newCanonical.Name, newCanonical.Id);

            return new TagNormalizerResult
            {
                CanonicalTag = newCanonical,
                RawTag = rawTag,
                Sentiment = sentiment,
                ConfidenceScore = 1.0,
                IsNewCanonical = true
            };
        }

        private static string BuildTagEmbeddingText(string name, string? descOrReason)
        {
            var n = (name ?? "").Trim().ToLowerInvariant().Replace('_', ' ');
            var d = (descOrReason ?? "").Trim();

            return string.IsNullOrWhiteSpace(d)
                ? $"Product feedback tag: {n}"
                : $"Product feedback tag: {n}. Meaning: {d}";
        }
    }
}
