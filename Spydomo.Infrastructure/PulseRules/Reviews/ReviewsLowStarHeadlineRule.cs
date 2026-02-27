using Microsoft.Extensions.Options;
using Spydomo.Common.Constants;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Text.Json;

namespace Spydomo.Infrastructure.PulseRules.Reviews
{
    /// <summary>
    /// Emits a Tier1 headline when a low-star review appears (e.g., ≤ 2★).
    /// Dedupe: uses type="Pain" + topic="low-star-review" in the observation index.
    /// </summary>
    [RuleMeta(Order = 10)] // run early; cheap
    public sealed class ReviewsLowStarHeadlineRule : IReviewsRule
    {
        private readonly PulseRulesOptions _opt;
        private readonly IPulseObservationRepository _obsRepo;

        public ReviewsLowStarHeadlineRule(IOptions<PulseRulesOptions> opt, IPulseObservationRepository obsRepo)
        {
            _opt = opt.Value;
            _obsRepo = obsRepo;
        }

        public bool IsMatch(SummarizedInfo si, TrackContext ctx)
            => PulseUtils.IsReviewSource(si.SourceTypeEnum) && TryGetStars(si.RawContent?.Content) is not null;

        public async Task<PulsePoint?> ProjectAsync(SummarizedInfo si, TrackContext ctx, CancellationToken ct = default)
        {
            var stars = TryGetStars(si.RawContent?.Content);
            if (stars is null) return null;

            // Threshold: configurable; default 2.0
            var threshold = _opt.LowStarThreshold <= 0 ? 2.0 : _opt.LowStarThreshold;
            if (stars.Value > threshold) return null;

            var company = si.Company?.Name ?? "Unknown";
            var source = si.SourceType?.Name ?? "Review";
            var now = ctx.NowUtc;

            // Choose a short evidence snippet
            var evidence = ExtractEvidence(si) ?? si.Gist ?? "Low-star review";

            // Dedupe topic: single canonical key for all low-star events
            const string observedType = "Pain";
            const string topic = "low-star-review";
            var topicKey = TopicKeyHelper.Slugify(topic);

            // Always record mention (for surge / 14-day aggregates)
            await _obsRepo.UpsertTodayAsync(si.CompanyId, observedType, topicKey, now, ct).ConfigureAwait(false);

            // Cool-down + surge override
            var shouldEmit = await DedupePolicy.ShouldEmitAsync(
                _obsRepo, si.CompanyId, observedType, topicKey, now, _opt, ct
            ).ConfigureAwait(false);

            if (!shouldEmit) return null;

            // Emit and stamp "last notified"
            await _obsRepo.SetLastNotifiedAtAsync(si.CompanyId, observedType, topicKey, now, ct).ConfigureAwait(false);

            var gist = si.Gist ?? "Very negative review reported";
            var title = $"{Math.Round(stars.Value):0}★ on {source}: {gist}";

            // Optional: volume guard — if the brand is very high-volume, keep Tier1 but rely on cooldown to avoid spam
            // var monthly = ctx.Baselines.ReviewsInLastDays(si.CompanyId, 30);

            return new PulsePoint(
                CompanyId: si.CompanyId,
                CompanyName: company,
                Bucket: PulseBucket.CustomerVoice,
                ChipSlug: SignalSlugs.PainSignal,
                Tier: PulseTier.Tier1,
                Title: title,
                Url: si.RawContent?.PostUrl ?? "",
                SeenAt: si.Date ?? now,
                Context: new()
                {
                    ["stars"] = stars,
                    ["source"] = source,
                    ["topic"] = topic,
                    ["headline"] = true
                },
                RawContentId: si.RawContentId,
                SummarizedInfoId: si.Id
            );
        }

        // --- helpers ---

        private static double? TryGetStars(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            if (NvarcharJson.TryGet<double>(json, "Metadata.Rating", out var rating)) return rating;
            if (NvarcharJson.TryGet<double>(json, "rating", out rating)) return rating;
            if (NvarcharJson.TryGet<double>(json, "overallRating", out rating)) return rating;
            return null;
        }

        private static string? ExtractEvidence(SummarizedInfo si)
        {
            // Prefer Capterra "cons", else "overall"; fallback to G2 raw text; else gist points
            var rc = si.RawContent?.Content;
            if (!string.IsNullOrWhiteSpace(rc))
            {
                if (NvarcharJson.TryGet<string>(rc, "Text.cons", out var cons) && !string.IsNullOrWhiteSpace(cons))
                    return cons.Trim();

                if (NvarcharJson.TryGet<string>(rc, "Text.overall", out var overall) && !string.IsNullOrWhiteSpace(overall))
                    return overall.Trim();

                // If G2 style flat text lives under "Text"
                if (NvarcharJson.TryGet<string>(rc, "Text", out var flat) && !string.IsNullOrWhiteSpace(flat))
                    return flat.Trim();
            }

            // fallback: first gist point
            if (!string.IsNullOrWhiteSpace(si.GistPointsJson))
            {
                try
                {
                    var arr = JsonSerializer.Deserialize<List<string>>(si.GistPointsJson);
                    var first = arr?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                    if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
                }
                catch { /* ignore */ }
            }

            return si.Gist;
        }
    }
}
