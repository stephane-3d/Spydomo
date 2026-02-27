using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Spydomo.Infrastructure.BackgroundServices
{
    // StrategicSummaryService.cs

    public class StrategicSummaryService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<StrategicSummaryService> _logger;
        private readonly IEnumerable<ITrackProcessor> _tracks;
        private readonly IPulseAgent _pulseAgent;
        
        public StrategicSummaryService(
            IDbContextFactory<SpydomoContext> dbFactory,
            ILogger<StrategicSummaryService> logger,
            IEnumerable<ITrackProcessor> tracks,
            IPulseAgent pulseAgent)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _tracks = tracks;
            _pulseAgent = pulseAgent;
        }

        public async Task<(List<StrategicSummary> Summaries, int MaxProcessedSummarizedInfoId)> GenerateSummaryForGroupAsync(
            int groupId,
            int lastProcessedId,
            string periodType = "daily",
            CancellationToken ct = default)
        {
            var opId = Guid.NewGuid().ToString("N")[..8];
            var sw = Stopwatch.StartNew();

            _logger.LogInformation("[SS:{Op}] START groupId={GroupId} lastProcessedId={LastProcessedId}",
                opId, groupId, lastProcessedId);

            var sis = await LoadSummarizedInfos(groupId, lastProcessedId, periodType, ct);

            if (!sis.Any())
            {
                _logger.LogInformation("[SS:{Op}] NO-OP no sis. elapsedMs={Ms}", opId, sw.ElapsedMilliseconds);
                return (new(), lastProcessedId);
            }

            var pulsePoints = new ConcurrentBag<PulsePoint>();

            try
            {
                // Track-by-track counts
                var grouped = _tracks.GroupBy(t => t.GetType()).ToList();
                foreach (var g in grouped.Where(g => g.Count() > 1))
                {
                    _logger.LogWarning("[SS:{Op}] Duplicate track injected: {Track} count={Count}",
                        opId, g.Key.Name, g.Count());
                }

                foreach (var track in grouped.Select(g => g.First()))
                {
                    ct.ThrowIfCancellationRequested();

                    var trackName = track.GetType().Name;
                    var before = pulsePoints.Count;

                    var ctx = track.BuildContext(groupId, sis);

                    int produced = 0;
                    await foreach (var p in track.EvaluateAsync(sis, ctx, ct).ConfigureAwait(false))
                    {
                        pulsePoints.Add(p);
                        produced++;
                    }

                }

                _logger.LogInformation("[SS:{Op}] PulsePoints raw total={Total}", opId, pulsePoints.Count);

                var deduped = DedupeByKey(pulsePoints)
                    .Select(p => p with { SourceKey = BuildSourceKey(p) })
                    .ToList();

                if (!deduped.Any())
                {
                    return (new(), lastProcessedId);
                }

                var now = DateTime.UtcNow;
                var periodStart = now.AddDays(-30);

                var context = new PulseAgentContext(
                    GroupId: groupId,
                    SummarizedInfos: sis,
                    CandidatePulsePoints: deduped,
                    PeriodStartUtc: periodStart,
                    PeriodEndUtc: now
                );

                var pulseBlurbs = await _pulseAgent.GeneratePulsesAsync(context, ct);

                if (!pulseBlurbs.Any())
                {
                    _logger.LogInformation("[SS:{Op}] NO-OP agent returned empty. elapsedMs={Ms}", opId, sw.ElapsedMilliseconds);
                    return (new(), lastProcessedId);
                }

                var summaries = MapToStrategicSummaries(groupId, pulseBlurbs.ToList());

                var maxId = sis.Max(x => x.Id);

                _logger.LogInformation("[SS:{Op}] DONE summaries={Count} maxProcessedId={MaxId} elapsedMs={Ms}",
                    opId, summaries.Count, maxId, sw.ElapsedMilliseconds);

                return (summaries, maxId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SS:{Op}] FAIL groupId={GroupId} elapsedMs={Ms}", opId, groupId, sw.ElapsedMilliseconds);
                return (new(), lastProcessedId);
            }
        }

        private static string BuildSourceKey(PulsePoint p)
        {
            // Best: stable ID when present (short + perfect uniqueness)
            if (p.SummarizedInfoId is int si) return $"si:{si}";
            if (p.RawContentId is int rc) return $"rc:{rc}";

            // URL fallback: hash a canonical string (short + stable)
            var url = NormalizeUrl(p.Url);
            if (!string.IsNullOrWhiteSpace(url))
            {
                // Include the dimensions that would otherwise collide
                var canonical = $"{p.CompanyId}|{p.Bucket}|{p.ChipSlug}|{url}";
                var h = Sha256Hex(canonical); // 64 hex chars

                // Keep it human-ish and still unique enough
                return $"url:{p.CompanyId}:{p.Bucket}:{p.ChipSlug}:{h}";
            }

            // Last resort: hash a canonical fallback (still short)
            var fallback = $"{p.CompanyId}|{p.Bucket}|{p.ChipSlug}|{p.SeenAt:O}|{p.Title}";
            return $"fb:{Sha256Hex(fallback)}";
        }

        private static string NormalizeUrl(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            raw = raw.Trim();

            // Try to canonicalize; if parsing fails, just lower-case trimmed string
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                return raw.ToLowerInvariant();

            // Lower host, strip fragment, normalize default ports, keep path+query
            var b = new UriBuilder(uri)
            {
                Fragment = "",
                Host = uri.Host.ToLowerInvariant()
            };

            // Remove default ports
            if ((b.Scheme == "https" && b.Port == 443) || (b.Scheme == "http" && b.Port == 80))
                b.Port = -1;

            // Optional: remove tracking params if you want more stability
            // (leave as-is for now unless you see duplicates)
            return b.Uri.ToString();
        }

        private static string Sha256Hex(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);

            // hex string (64 chars)
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }


        private async Task<List<SummarizedInfo>> LoadSummarizedInfos(
            int groupId,
            int lastProcessedId,
            string periodType = "daily",
            CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // SIs already used by StrategicSummaries for that group+period
            var alreadyDoneSiIds =
                db.StrategicSummaries.AsNoTracking()
                  .Where(s => s.CompanyGroupId == groupId
                           && s.PeriodType == periodType
                           && s.SummarizedInfoId != null)
                  .Select(s => s.SummarizedInfoId!.Value);

            // Get candidate SI ids (distinct!)
            var siIds = await (
                from tcg in db.TrackedCompanyGroups.AsNoTracking()
                join tc in db.TrackedCompanies.AsNoTracking() on tcg.TrackedCompanyId equals tc.Id
                join si in db.SummarizedInfos.AsNoTracking() on tc.CompanyId equals si.CompanyId
                where tcg.CompanyGroupId == groupId
                   && si.ProcessingStatus >= SummarizedInfoProcessingStatus.GistReady
                   && si.Id > lastProcessedId
                   && !alreadyDoneSiIds.Contains(si.Id)
                orderby si.Id
                select si.Id
            ).Distinct().Take(500).ToListAsync(ct);

            var sis = await db.SummarizedInfos.AsNoTracking()
                .Where(x => siIds.Contains(x.Id))
                .AsSplitQuery()
                .Include(x => x.SourceType)
                .Include(x => x.RawContent)
                .Include(x => x.Company)
                .Include(x => x.SummarizedInfoThemes)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);


            return sis;
        }


        private static List<PulsePoint> DedupeByKey(IEnumerable<PulsePoint> points)
        {
            string Key(PulsePoint p)
            {
                var day = (p.SeenAt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(p.SeenAt, DateTimeKind.Utc) : p.SeenAt).Date;
                var normTitle = (p.Title ?? "").Trim().ToLowerInvariant();
                return $"{p.CompanyId}|{p.Bucket}|{p.ChipSlug}|{normTitle}|{day:yyyyMMdd}";
            }

            var dict = new Dictionary<string, PulsePoint>();
            foreach (var p in points)
            {
                var k = Key(p);
                if (!dict.ContainsKey(k)) dict[k] = p;
            }
            return dict.Values.ToList();
        }

        private List<StrategicSummary> MapToStrategicSummaries(int groupId, List<PulseBlurb> blurbs)
        {
            return blurbs.Select(b => new StrategicSummary
            {
                CompanyGroupId = groupId,
                CompanyId = b.CompanyId,
                PeriodType = "daily",
                SourceKey = b.SourceKey,
                SummaryText = b.Blurb,
                RawContentId = b.RawContentId,
                SummarizedInfoId = b.SummarizedInfoId,
                Url = b.Url ?? string.Empty,
                CreatedOn = DateTime.UtcNow,
                IncludedSignalTypes = new List<string> { b.Chip },
                Tier = (PulseTier)(int)b.Tier,
                TierReason = b.TierReason ?? string.Empty
            }).ToList();
        }
    }

}
