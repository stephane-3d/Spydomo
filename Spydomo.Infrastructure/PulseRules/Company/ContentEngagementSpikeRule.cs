using Spydomo.Common.Constants;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Collections.Concurrent;

namespace Spydomo.Infrastructure.PulseRules.CompanyContent
{
    // Detects unusually high engagement on company posts relative to recent baseline (from CompanyStats)
    [RuleMeta(Order = 30)]
    public sealed class ContentEngagementSpikeRule : ICompanyContentRule
    {
        private readonly IEngagementStatsRepository _statsRepo;
        private readonly ConcurrentDictionary<(int CompanyId, int? SourceTypeId, string Period, DateTime EndDate), double> _baselineCache = new();

        public ContentEngagementSpikeRule(IEngagementStatsRepository statsRepo)
        {
            _statsRepo = statsRepo;
        }

        public bool IsMatch(SummarizedInfo si, TrackContext ctx)
            => PulseUtils.IsContentSource(si.SourceTypeEnum);

        public async Task<PulsePoint?> ProjectAsync(SummarizedInfo si, TrackContext ctx, CancellationToken ct = default)
        {
            var companyId = si.CompanyId;
            var company = si.Company?.Name ?? "Unknown";
            var source = si.SourceType?.Name ?? "Unknown";
            var raw = si.RawContent?.Content ?? "";

            var engagement = ExtractEngagement(raw);
            if (engagement.Total <= 0) return null;

            var cacheKey = (companyId, si.SourceTypeId, "30d", ctx.NowUtc.Date);

            var baseline = _baselineCache.TryGetValue(cacheKey, out var cached)
                ? cached
                : (_baselineCache[cacheKey] = await _statsRepo
                    .GetBaselineAsync(companyId, si.SourceTypeId, ctx.NowUtc, "30d", ct)
                    .ConfigureAwait(false));

            if (baseline <= 0) return null;

            var ratio = engagement.Total / baseline;
            if (ratio < 2.0) return null; // require >= 2× to count as a spike

            var tier = ratio switch
            {
                >= 4.0 => PulseTier.Tier1,
                >= 2.5 => PulseTier.Tier2,
                _ => PulseTier.Tier3
            };

            var headline = $"{source} post generated {ratio:F1}× higher engagement than baseline";

            return new PulsePoint(
                CompanyId: companyId,
                CompanyName: company,
                Bucket: PulseBucket.CompanyActivity,
                ChipSlug: SignalSlugs.EngagementSpike,
                Tier: tier,
                Title: headline,
                Url: si.RawContent?.PostUrl ?? "",
                SeenAt: si.Date ?? ctx.NowUtc,
                Context: new()
                {
                    ["likes"] = engagement.Likes,
                    ["comments"] = engagement.Comments,
                    ["shares"] = engagement.Shares,
                    ["baseline"] = baseline,
                    ["ratio"] = ratio,
                    ["source"] = source
                },
                RawContentId: si.RawContentId,
                SummarizedInfoId: si.Id
            );
        }

        // Be lenient across platforms (LinkedIn: reactions; X: reposts; etc.)
        private static (int Likes, int Comments, int Shares, double Total) ExtractEngagement(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return (0, 0, 0, 0);

            int likes = 0, comments = 0, shares = 0;

            // Prefer canonical keys; fall back to common aliases
            likes = TryGetInt(json, "likes") ?? TryGetInt(json, "reactions") ?? 0;
            comments = TryGetInt(json, "comments") ?? TryGetInt(json, "commentCount") ?? 0;
            shares = TryGetInt(json, "shares") ?? TryGetInt(json, "reposts") ?? TryGetInt(json, "retweets") ?? 0;

            return (likes, comments, shares, likes + comments + shares);

            static int? TryGetInt(string j, string path)
                => NvarcharJson.TryGet<int>(j, path, out var v) ? v : null;
        }
    }
}
