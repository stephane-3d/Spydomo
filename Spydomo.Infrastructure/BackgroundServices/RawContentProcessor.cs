using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class RawContentProcessor
    {
        private readonly IAiSummarizer _aiSummarizer;
        private readonly IContentAdapter _contentAdapter;
        private readonly ITagNormalizer _tagNormalizer;
        private readonly IThemeNormalizer _themeNormalizer;
        private readonly ILogger<RawContentProcessor> _logger;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public RawContentProcessor(
            IAiSummarizer aiSummarizer,
            IContentAdapter contentAdapter,
            ITagNormalizer tagNormalizer,
            IThemeNormalizer themeNormalizer,
            ILogger<RawContentProcessor> logger,
            IDbContextFactory<SpydomoContext> dbFactory)
        {
            _aiSummarizer = aiSummarizer;
            _contentAdapter = contentAdapter;
            _tagNormalizer = tagNormalizer;
            _themeNormalizer = themeNormalizer;
            _logger = logger;
            _dbFactory = dbFactory;
        }

        public async Task<int> ProcessBatchAsync(List<int> rawContentIds, CancellationToken ct = default)
        {
            if (rawContentIds == null || rawContentIds.Count == 0) return 0;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Load all raws in one query
            var raws = await db.RawContents
                .Where(r => rawContentIds.Contains(r.Id))
                .ToListAsync(ct);

            if (raws.Count == 0) return 0;

            // Build canonical text
            var batch = new List<(int Id, string CanonicalText, OriginTypeEnum OriginType)>(raws.Count);
            var emptyTextIds = new List<int>();

            foreach (var raw in raws)
            {
                var canonicalText = _contentAdapter.GetCanonicalText(raw);
                if (string.IsNullOrWhiteSpace(canonicalText))
                {
                    emptyTextIds.Add(raw.Id);
                    continue;
                }
                batch.Add((raw.Id, canonicalText, raw.OriginType));
            }

            // Mark empties as SKIPPED fast
            if (emptyTextIds.Count > 0)
            {
                await db.RawContents
                    .Where(r => emptyTextIds.Contains(r.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, RawContentStatusEnum.SKIPPED)
                        .SetProperty(x => x.ProcessingAt, (DateTime?)null),
                        ct);
            }

            if (batch.Count == 0) return 0;

            var companyId = raws[0].CompanyId; // safe now

            // ONE AI call per origin group (inside SummarizeBatchAsync)
            var summaryMap = await _aiSummarizer.SummarizeBatchAsync(batch, companyId, ct);

            var processedOk = 0;

            // Now commit each raw. Keep it simple first; optimize further after.
            foreach (var raw in raws)
            {
                ct.ThrowIfCancellationRequested();

                if (!summaryMap.TryGetValue(raw.Id, out var summary))
                {
                    raw.Status = RawContentStatusEnum.FAILED;
                    raw.ProcessingAt = null;
                    continue;
                }

                var signalScore = await SignalScoreCalculator.FromRawContent(raw);

                // Dedup
                var distinctTags = summary.Tags
                    .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var distinctThemes = summary.Themes
                    .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var distinctSignals = summary.SignalTypes
                    .GroupBy(s => s.SignalTypeId)
                    .Select(g => g.First())
                    .ToList();

                // Normalize in parallel (usually a huge speedup)
                var tagTasks = distinctTags.Select(async tag =>
                {
                    var result = await _tagNormalizer.NormalizeAsync(tag.Key, tag.Value, raw.CompanyId);
                    return (Label: tag.Key, Reason: tag.Value, CanonicalId: result.CanonicalTag?.Id, Confidence: 1.0, Sentiment: result.Sentiment);
                }).ToList();

                var themeTasks = distinctThemes.Select(async theme =>
                {
                    var result = await _themeNormalizer.NormalizeAsync(theme.Key, theme.Value, raw.CompanyId);
                    return (Label: theme.Key, Reason: theme.Value, CanonicalId: result.CanonicalTheme?.Id, Confidence: 1.0);
                }).ToList();

                var normalizedTags = await Task.WhenAll(tagTasks);
                var normalizedThemes = await Task.WhenAll(themeTasks);

                // Transaction for this raw only (fast)
                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);

                    var existingId = await db.SummarizedInfos
                        .Where(s => s.RawContentId == raw.Id)
                        .Select(s => (int?)s.Id)
                        .FirstOrDefaultAsync(ct);

                    if (existingId.HasValue)
                    {
                        var sid = existingId.Value;
                        await db.SummarizedInfoTags.Where(x => x.SummarizedInfoId == sid).ExecuteDeleteAsync(ct);
                        await db.SummarizedInfoThemes.Where(x => x.SummarizedInfoId == sid).ExecuteDeleteAsync(ct);
                        await db.SummarizedInfoSignalTypes.Where(x => x.SummarizedInfoId == sid).ExecuteDeleteAsync(ct);
                        await db.SummarizedInfos.Where(x => x.Id == sid).ExecuteDeleteAsync(ct);
                    }

                    var summarizedInfo = new SummarizedInfo
                    {
                        RawContentId = raw.Id,
                        CompanyId = raw.CompanyId,
                        SourceTypeId = raw.DataSourceTypeId,
                        Gist = summary.Gist,
                        GistGeneratedAt = DateTime.UtcNow,
                        GistSource = "gpt-4",
                        GistPointsJson = JsonSerializer.Serialize(summary.Points),
                        Date = raw.PostedDate ?? raw.CreatedAt ?? DateTime.UtcNow,
                        OriginType = raw.OriginType,
                        SignalScore = signalScore,
                        ProcessingStatus = SummarizedInfoProcessingStatus.GistReady,
                        SentimentReason = summary.Sentiment.Reason,
                        Sentiment = Enum.TryParse<SentimentEnum>(summary.Sentiment.Label, out var sent) ? sent : (SentimentEnum?)null,
                    };

                    db.SummarizedInfos.Add(summarizedInfo);

                    foreach (var t in normalizedTags)
                    {
                        db.SummarizedInfoTags.Add(new SummarizedInfoTag
                        {
                            Label = t.Label,
                            Reason = t.Reason,
                            CanonicalTagId = t.CanonicalId,
                            ConfidenceScore = t.Confidence,
                            Sentiment = t.Sentiment,
                            SummarizedInfo = summarizedInfo
                        });
                    }

                    foreach (var th in normalizedThemes)
                    {
                        db.SummarizedInfoThemes.Add(new SummarizedInfoTheme
                        {
                            Label = th.Label,
                            Reason = th.Reason,
                            CanonicalThemeId = th.CanonicalId,
                            ConfidenceScore = th.Confidence,
                            SummarizedInfo = summarizedInfo
                        });
                    }

                    foreach (var s in distinctSignals)
                    {
                        db.SummarizedInfoSignalTypes.Add(new SummarizedInfoSignalType
                        {
                            SignalTypeId = s.SignalTypeId,
                            Reason = s.Reason,
                            SummarizedInfo = summarizedInfo
                        });
                    }

                    raw.Status = RawContentStatusEnum.DONE;
                    raw.ProcessingAt = null;

                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                });

                processedOk++;
            }

            return processedOk;
        }

        public async Task<bool> ProcessAsync(int rawContentId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var raw = await db.RawContents.FirstOrDefaultAsync(r => r.Id == rawContentId, ct);
            if (raw == null)
            {
                _logger.LogWarning("RawContent ID {RawId} not found.", rawContentId);
                return false;
            }

            try
            {
                var canonicalText = _contentAdapter.GetCanonicalText(raw);
                if (string.IsNullOrWhiteSpace(canonicalText))
                {
                    raw.Status = RawContentStatusEnum.SKIPPED;
                    raw.ProcessingAt = null;
                    await db.SaveChangesAsync(ct);
                    return false;
                }

                // AI outside tx (cancelable)
                var summaryMap = await _aiSummarizer.SummarizeBatchAsync(
                    new() { (raw.Id, canonicalText, raw.OriginType) },
                    raw.CompanyId);

                _logger.LogInformation("... Ai Summarizer called for {RawContentId}", raw.Id);

                if (!summaryMap.TryGetValue(raw.Id, out var summary))
                {
                    raw.Status = RawContentStatusEnum.FAILED;
                    raw.ProcessingAt = null;
                    await db.SaveChangesAsync(ct);
                    return false;
                }

                var signalScore = await SignalScoreCalculator.FromRawContent(raw);

                // ---------- PREP OUTSIDE TX (slow stuff) ----------
                var distinctTags = summary.Tags
                    .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var distinctThemes = summary.Themes
                    .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var distinctSignals = summary.SignalTypes
                    .GroupBy(s => s.SignalTypeId)
                    .Select(g => g.First())
                    .ToList();

                var normalizedTags = new List<(string Label, string Reason, int? CanonicalId, double Confidence, string? Sentiment)>();
                foreach (var tag in distinctTags)
                {
                    var result = await _tagNormalizer.NormalizeAsync(tag.Key, tag.Value, raw.CompanyId);
                    normalizedTags.Add((tag.Key, tag.Value, result.CanonicalTag?.Id, 1.0, result.Sentiment));
                }

                var normalizedThemes = new List<(string Label, string Reason, int? CanonicalId, double Confidence)>();
                foreach (var theme in distinctThemes)
                {
                    var result = await _themeNormalizer.NormalizeAsync(theme.Key, theme.Value, raw.CompanyId);
                    normalizedThemes.Add((theme.Key, theme.Value, result.CanonicalTheme?.Id, 1.0));
                }

                // ---------- TX / COMMIT (fast DB only) ----------
                using var commitCts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // or CancellationToken.None
                var dbCt = commitCts.Token;

                var strategy = db.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(dbCt);

                    var existingId = await db.SummarizedInfos
                        .Where(s => s.RawContentId == raw.Id)
                        .Select(s => (int?)s.Id)
                        .FirstOrDefaultAsync(dbCt);

                    if (existingId.HasValue)
                    {
                        var sid = existingId.Value;
                        await db.SummarizedInfoTags.Where(x => x.SummarizedInfoId == sid).ExecuteDeleteAsync(dbCt);
                        await db.SummarizedInfoThemes.Where(x => x.SummarizedInfoId == sid).ExecuteDeleteAsync(dbCt);
                        await db.SummarizedInfoSignalTypes.Where(x => x.SummarizedInfoId == sid).ExecuteDeleteAsync(dbCt);
                        await db.SummarizedInfos.Where(x => x.Id == sid).ExecuteDeleteAsync(dbCt);
                    }

                    var summarizedInfo = new SummarizedInfo
                    {
                        RawContentId = raw.Id,
                        CompanyId = raw.CompanyId,
                        SourceTypeId = raw.DataSourceTypeId,
                        Gist = summary.Gist,
                        GistGeneratedAt = DateTime.UtcNow,
                        GistSource = "gpt-4",
                        GistPointsJson = JsonSerializer.Serialize(summary.Points),
                        Date = raw.PostedDate ?? raw.CreatedAt ?? DateTime.UtcNow,
                        OriginType = raw.OriginType,
                        SignalScore = signalScore,
                        ProcessingStatus = SummarizedInfoProcessingStatus.GistReady,
                        SentimentReason = summary.Sentiment.Reason,
                        Sentiment = Enum.TryParse<SentimentEnum>(summary.Sentiment.Label, out var sentiment)
                            ? sentiment
                            : (SentimentEnum?)null,
                    };

                    db.SummarizedInfos.Add(summarizedInfo);

                    foreach (var t in normalizedTags)
                    {
                        db.SummarizedInfoTags.Add(new SummarizedInfoTag
                        {
                            Label = t.Label,
                            Reason = t.Reason,
                            CanonicalTagId = t.CanonicalId,
                            ConfidenceScore = t.Confidence,
                            Sentiment = t.Sentiment,
                            SummarizedInfo = summarizedInfo
                        });
                    }

                    foreach (var th in normalizedThemes)
                    {
                        db.SummarizedInfoThemes.Add(new SummarizedInfoTheme
                        {
                            Label = th.Label,
                            Reason = th.Reason,
                            CanonicalThemeId = th.CanonicalId,
                            ConfidenceScore = th.Confidence,
                            SummarizedInfo = summarizedInfo
                        });
                    }

                    foreach (var s in distinctSignals)
                    {
                        db.SummarizedInfoSignalTypes.Add(new SummarizedInfoSignalType
                        {
                            SignalTypeId = s.SignalTypeId,
                            Reason = s.Reason,
                            SummarizedInfo = summarizedInfo
                        });
                    }

                    raw.Status = RawContentStatusEnum.DONE;
                    raw.ProcessingAt = null;

                    await db.SaveChangesAsync(dbCt);
                    await tx.CommitAsync(dbCt);
                });

                _logger.LogInformation("... SummarizedInfo creation completed for {RawContentId}", raw.Id);

                return true;
            }
            catch (OperationCanceledException oce) when (ct.IsCancellationRequested)
            {
                // Job canceled (shutdown / timeout / manual). Don’t treat as “error”.
                _logger.LogWarning(oce, "RawContent processing canceled. RawContentId={RawId}", raw.Id);

                // Best-effort: mark failed or revert to NEW so it can retry.
                try
                {
                    raw.Status = RawContentStatusEnum.FAILED; // or NEW if you prefer automatic retry
                    raw.ProcessingAt = null;
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch { }

                return false;
            }
            catch (Exception ex)
            {
                try
                {
                    raw.Status = RawContentStatusEnum.FAILED;
                    raw.ProcessingAt = null;
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch { }

                _logger.LogError(ex, "❌ Exception while processing RawContent ID {RawId}", raw.Id);
                return false;
            }
        }
    }
}
