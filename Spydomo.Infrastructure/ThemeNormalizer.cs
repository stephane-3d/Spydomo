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
using static Spydomo.Infrastructure.AiServices.OpenAiEmbeddingService;

namespace Spydomo.Infrastructure
{
    public class ThemeNormalizer : IThemeNormalizer
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly OpenAiEmbeddingService _embeddingService;
        private readonly CanonicalThemeEmbeddingCache _embeddingCache;
        private readonly ISlugService _slugService;
        private readonly ILogger<ThemeNormalizer> _logger;

        public ThemeNormalizer(IDbContextFactory<SpydomoContext> dbFactory, OpenAiEmbeddingService embeddingService,
            CanonicalThemeEmbeddingCache embeddingCache, ISlugService slugService,
            ILogger<ThemeNormalizer> logger)
        {
            _dbFactory = dbFactory;
            _embeddingService = embeddingService;
            _embeddingCache = embeddingCache;
            _slugService = slugService;
            _logger = logger;
        }


        public async Task<ThemeNormalizerResult> NormalizeAsync(string rawTheme, string reason, int? companyId = null, CancellationToken ct = default)
        {
            const double MinScore = 0.90;
            const double MinMargin = 0.015;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // When to call LLM judge (only on ambiguous cases)
            const double JudgeMinScore = 0.84; // below that, usually create new
            const double JudgeMaxScore = 0.92; // above that, embeddings already confident
            const int JudgeTopN = 5;
            const double JudgeMinConfidence = 0.75;

            var cleaned = (rawTheme ?? "").Trim().ToLowerInvariant();

            // 1) Exact match
            var canonical = await db.CanonicalThemes.FirstOrDefaultAsync(t => t.Name == cleaned, ct);
            if (canonical != null)
            {
                _logger.LogInformation("ThemeNormalizer exact match raw='{Raw}' -> canonicalId={Id}", cleaned, canonical.Id);
                return new ThemeNormalizerResult
                {
                    CanonicalTheme = canonical,
                    RawTheme = cleaned,
                    ConfidenceScore = 1.0,
                    IsNewCanonical = false
                };
            }

            // 2) Embedding match
            var embeddingInput = BuildThemeEmbeddingText(cleaned, reason);
            var rawEmbedding = await _embeddingService.GetEmbeddingAsync(embeddingInput, companyId);

            var canonicals = await _embeddingCache.GetCanonicalThemesAsync(); // CachedCanonicalTheme(Id, Name, Embedding)
            if (canonicals.Count > 0)
            {
                var candidates = canonicals
                    .Select(c => (Item: c, Embedding: (IReadOnlyList<float>)c.Embedding))
                    .ToList();

                var (bestCandidate, bestScore, secondCandidate, secondScore)
                    = SimilarityHelper.FindTop2EmbeddingMatch(rawEmbedding, candidates);

                var margin = bestScore - secondScore;

                _logger.LogInformation(
                    "ThemeNormalizer embedding raw='{Raw}' best={BestName}({BestId}) score={BestScore:F4} second={SecondName}({SecondId}) score={SecondScore:F4} margin={Margin:F4}",
                    cleaned,
                    bestCandidate?.Name, bestCandidate?.Id, bestScore,
                    secondCandidate?.Name, secondCandidate?.Id, secondScore,
                    margin);

                // 2a) Confident embedding match
                if (bestCandidate != null && bestScore >= MinScore && margin >= MinMargin)
                {
                    var matchedTheme = await db.CanonicalThemes.FindAsync(new object?[] { bestCandidate.Id }, ct);
                    _logger.LogInformation("ThemeNormalizer ACCEPT embedding match raw='{Raw}' -> canonicalId={Id} score={Score:F4}", cleaned, bestCandidate.Id, bestScore);

                    return new ThemeNormalizerResult
                    {
                        CanonicalTheme = matchedTheme!,
                        RawTheme = cleaned,
                        ConfidenceScore = bestScore,
                        IsNewCanonical = false
                    };
                }

                // 2b) Ambiguous? Ask LLM judge (sustainable alternative to KeywordPenalty lists)
                var shouldJudge =
                    bestCandidate != null &&
                    bestScore >= JudgeMinScore &&
                    bestScore <= JudgeMaxScore &&
                    margin < MinMargin;

                if (shouldJudge)
                {
                    var top = SimilarityHelper.GetTopN(rawEmbedding, candidates, JudgeTopN);
                    var topIds = top.Select(x => x.Item.Id).ToList();

                    // Pull full canonical rows (Name + Description)
                    var dbThemes = await db.CanonicalThemes
                        .AsNoTracking()
                        .Where(t => topIds.Contains(t.Id))
                        .ToListAsync(ct);

                    // Preserve the embedding rank order (top similarity first)
                    var judgeCandidates = top.Select(x =>
                    {
                        var t = dbThemes.First(z => z.Id == x.Item.Id);
                        return new ThemeJudgeCandidate(
                            t.Id,
                            t.Name,
                            BuildCanonicalDefinition(t)
                        );
                    }).ToList();

                    _logger.LogInformation("ThemeNormalizer JUDGE triggered raw='{Raw}' bestScore={BestScore:F4} margin={Margin:F4}", cleaned, bestScore, margin);

                    var judge = await _embeddingService.JudgeThemeMatchAsync(
                        rawTheme: cleaned,
                        reason: reason,
                        candidates: judgeCandidates,
                        companyId: companyId,
                        ct: ct);

                    _logger.LogInformation(
                        "ThemeNormalizer JUDGE result raw='{Raw}' decision={Decision} bestId={BestId} conf={Conf:F2} rationale='{Rationale}'",
                        cleaned, judge.decision, judge.bestId, judge.confidence, judge.rationale);

                    if (judge.decision == "match" && judge.bestId.HasValue && judge.confidence >= JudgeMinConfidence)
                    {
                        var matched = await db.CanonicalThemes.FindAsync(new object?[] { judge.bestId.Value }, ct);
                        if (matched != null)
                        {
                            return new ThemeNormalizerResult
                            {
                                CanonicalTheme = matched,
                                RawTheme = cleaned,
                                ConfidenceScore = Math.Max(bestScore, judge.confidence), // your choice
                                IsNewCanonical = false
                            };
                        }
                    }
                }
            }

            // 3) Create new canonical
            _logger.LogInformation("ThemeNormalizer CREATE NEW canonical raw='{Raw}' (no confident match)", cleaned);

            var newEmbeddingJson = JsonSerializer.Serialize(rawEmbedding);

            var newCanonical = new CanonicalTheme
            {
                Name = cleaned,
                Description = reason,
                EmbeddingJson = newEmbeddingJson,
                CreatedAt = DateTime.UtcNow,
                Slug = await _slugService.GenerateUniqueSlugAsync(cleaned, EntityType.Theme)
            };

            db.CanonicalThemes.Add(newCanonical);
            await db.SaveChangesAsync(ct);

            _embeddingCache.Invalidate();

            return new ThemeNormalizerResult
            {
                CanonicalTheme = newCanonical,
                RawTheme = cleaned,
                ConfidenceScore = 1.0,
                IsNewCanonical = true
            };
        }

        public static string BuildThemeEmbeddingText(string name, string? descOrReason)
        {
            var n = (name ?? "").Trim().ToLowerInvariant().Replace('_', ' ');
            var d = (descOrReason ?? "").Trim();

            return string.IsNullOrWhiteSpace(d)
                ? $"Product feedback theme: {n}"
                : $"Product feedback theme: {n}. Meaning: {d}";
        }

        static string BuildCanonicalDefinition(CanonicalTheme t)
        {
            // Humanize the name as a fallback
            static string Humanize(string s) => (s ?? "")
                .Trim()
                .Replace('_', ' ')
                .ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(t.Description))
                return t.Description!.Trim();

            // If no description, at least give the judge something grounded
            return $"Theme about: {Humanize(t.Name)}";
        }

    }

}
