using Microsoft.Extensions.Options;
using Spydomo.Common.Constants;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Spydomo.Infrastructure.PulseRules.CompanyContent
{
    [RuleMeta(Order = 40)]
    public sealed class PostingFrequencySpikeRule : ICompanyContentRule
    {
        private readonly IPostingWindowStatsRepository _postingStatsRepo;
        private readonly ConcurrentDictionary<(int CompanyId, string Period), PostingWindowStats?> _cache = new();

        private readonly IPulseObservationRepository _obsRepo;
        private readonly PulseRulesOptions _opt;

        // Optional knobs; could move to PulseRulesOptions later
        private readonly string _defaultPeriod = "30d";
        private readonly double _t3Ratio = 1.75;
        private readonly double _t2Ratio = 2.50;
        private readonly double _t1Ratio = 3.50;
        private readonly int _t3MinPosts = 6;
        private readonly int _t2MinPosts = 8;
        private readonly int _t1MinPosts = 12;

        public PostingFrequencySpikeRule(IPostingWindowStatsRepository postingStatsRepo, IPulseObservationRepository obsRepo, IOptions<PulseRulesOptions> opt)
        {
            _postingStatsRepo = postingStatsRepo;
            _obsRepo = obsRepo;
            _opt = opt.Value;
        }

        public bool IsMatch(SummarizedInfo si, TrackContext ctx)
            => PulseUtils.IsContentSource(si.SourceTypeEnum);

        public async Task<PulsePoint?> ProjectAsync(SummarizedInfo si, TrackContext ctx, CancellationToken ct = default)
        {
            var key = (si.CompanyId, _defaultPeriod);

            // cache per-run (rule instance lifetime) to avoid repeated queries
            var stats = _cache.TryGetValue(key, out var cached)
                ? cached
                : (_cache[key] = await _postingStatsRepo.GetAsync(si.CompanyId, _defaultPeriod, ct).ConfigureAwait(false));

            if (stats is null) return null;

            var curr = stats.CurrentPosts;
            var prev = Math.Max(stats.PreviousPosts, 0);
            if (curr <= 0) return null;

            // Compute ratio & tier
            PulseTier? tier = null;
            double ratio;

            if (prev == 0)
            {
                // New activity where there was none
                if (curr >= _t2MinPosts) { tier = PulseTier.Tier2; ratio = double.PositiveInfinity; }
                else return null;
            }
            else
            {
                ratio = (double)curr / prev;
                if (ratio >= _t1Ratio && curr >= _t1MinPosts) tier = PulseTier.Tier1;
                else if (ratio >= _t2Ratio && curr >= _t2MinPosts) tier = PulseTier.Tier2;
                else if (ratio >= _t3Ratio && curr >= _t3MinPosts) tier = PulseTier.Tier3;
                else return null;
            }

            // Dedupe: one emit per period per company
            var companyId = si.CompanyId;
            var typeKey = $"PostingFrequency:{_defaultPeriod}:{stats.EndDate:yyyyMMdd}";
            var nowUtc = ctx.NowUtc;

            await _obsRepo.UpsertTodayAsync(companyId, "PostingFrequency", typeKey, nowUtc, ct).ConfigureAwait(false);
            var shouldEmit = await DedupePolicy
                .ShouldEmitAsync(_obsRepo, companyId, "PostingFrequency", typeKey, nowUtc, _opt, ct)
                .ConfigureAwait(false);
            if (!shouldEmit) return null;

            await _obsRepo.SetLastNotifiedAtAsync(companyId, "PostingFrequency", typeKey, nowUtc, ct).ConfigureAwait(false);

            // Optional: extract top posting source this period from SourceBreakdownJson
            var (topSource, topPosts) = TryGetTopCompanySource(stats.SourceBreakdown);

            var ratioText = prev == 0 ? "from zero baseline" : $"{(double)curr / Math.Max(prev, 1):F1}× vs prior";
            var title = topSource is null
                ? $"Posting cadence up {ratioText} ({curr} vs {prev} posts, {_defaultPeriod})"
                : $"{topSource} posting up {ratioText} ({curr} vs {prev} posts, {_defaultPeriod})";

            return new PulsePoint(
                CompanyId: companyId,
                CompanyName: si.Company?.Name ?? "Unknown",
                Bucket: PulseBucket.CompanyActivity,
                ChipSlug: SignalSlugs.MarketingTactic, // consider a new enum like PostingSpike later
                Tier: tier!.Value,
                Title: title,
                Url: si.RawContent?.PostUrl ?? "",
                SeenAt: si.Date ?? ctx.NowUtc,
                Context: new()
                {
                    ["spikeType"] = "PostingFrequency",
                    ["periodType"] = _defaultPeriod,
                    ["periodStart"] = stats.StartDate,
                    ["periodEnd"] = stats.EndDate,
                    ["currentPosts"] = curr,
                    ["previousPosts"] = prev,
                    ["ratio"] = prev == 0 ? null : (double)curr / prev,
                    ["topSource"] = topSource,
                    ["topSourcePosts"] = topPosts
                },
                RawContentId: si.RawContentId,
                SummarizedInfoId: si.Id
            );
        }

        private static (string? Source, int Count) TryGetTopCompanySource(Dictionary<string, int>? breakdown)
        {
            if (breakdown == null || breakdown.Count == 0)
                return (null, 0);

            var top = breakdown
                .OrderByDescending(kv => kv.Value)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(top.Key) || top.Value <= 0)
                return (null, 0);

            // Optional: prettify keys like "linkedin" -> "LinkedIn"
            var pretty = top.Key switch
            {
                "linkedin" => "LinkedIn",
                "x" => "X",
                "youtube" => "YouTube",
                _ => char.ToUpperInvariant(top.Key[0]) + top.Key[1..]
            };

            return (pretty, top.Value);
        }
    }
}
