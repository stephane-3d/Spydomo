using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Net.Http.Json;
using System.Text.Json;
using Spydomo.Common.Constants;

namespace Spydomo.Infrastructure.PulseRules.Reviews
{
    // This rule covers pain signals, praises and feature request that can be extracted from a review
    [RuleMeta(Order = 20)]
    public sealed class ReviewsObservationRule : IReviewsRule
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _cfg;
        private readonly PulseRulesOptions _opt;
        private readonly IPulseObservationRepository _obsRepo;

        public ReviewsObservationRule(HttpClient http, IConfiguration cfg, IOptions<PulseRulesOptions> opt, IPulseObservationRepository obsRepo)
        {
            _http = http;
            _cfg = cfg;
            _opt = opt.Value;
            _obsRepo = obsRepo;
        }

        public bool IsMatch(SummarizedInfo si, TrackContext ctx)
            => PulseUtils.IsReviewSource(si.SourceTypeEnum);

        public async Task<PulsePoint?> ProjectAsync(SummarizedInfo si, TrackContext ctx, CancellationToken ct = default)
        {
            var company = si.Company?.Name ?? "Unknown";
            var source = si.SourceType?.Name ?? "Unknown";
            var gist = si.Gist ?? "";
            var points = ParseGistPoints(si.GistPointsJson);
            var raw = si.RawContent?.Content ?? "";

            // --- Build prompts (company-agnostic, colleague tone)
            var system = @"You are a competitive intelligence formatter.

                You will receive one review-like item with:
                - Gist (1–2 sentences)
                - GistPoints (bullets)
                - RawContent (short text or small JSON from Capterra/G2)

                Extract 0–3 observations of these types:
                - Pain: user frustration, friction, complexity, limits, costs
                - FeatureRequest: explicit request verbs only (""wish"", ""need"", ""please add"", ""missing"", ""should have"")
                - Praise: clear positive signals; phrase through a positioning lens (why users like them)

                Star ratings:
                - Low stars (≤ 3.0) usually mean Pain deserves Tier1; FeatureRequest can be Tier1 when clearly tied to dissatisfaction.
                - High stars (≥ 4.5) usually mean Praise → Tier3; Pain should be treated as minor unless severe.

                Blurb style:
                - ONE colleague-style sentence (≤ 24 words). Do NOT prefix with company name.
                - Use neutral phrasing for single reviews; avoid pluralization like ""users/customers"" unless the input explicitly shows multiple people.
                  Examples (good): ""Drag-and-drop feels clunky."", ""Widget edits often require starting over.""
                  Avoid for single reviews: ""Users report..."", ""Customers say...""

                For EACH observation return:
                - type: Pain | FeatureRequest | Praise
                - tier: Tier1 for Pain; Tier2 for FeatureRequest; Tier3 for Praise
                - topic: short canonical label (e.g., ""widget editing friction"", ""csv import request"", ""hands-on support praise"")
                - blurb: ONE colleague-style sentence (≤ 24 words)
                - evidence: short exact quote or near-quote (≤ 20 words)
                - confidence: 0..1

                Hard rules:
                - Only create FeatureRequest if explicit request verbs are present; otherwise treat as Pain if appropriate.
                - Do not exaggerate scope: if the input is one review, do not imply many users (no ""users/customers"" unless explicitly plural in the input).
                - Keep each observation independent; 0–3 total.
                - Output strict JSON:
                { ""observations"": [ { ""type"":""Pain|FeatureRequest|Praise"", ""tier"":""Tier1|Tier2|Tier3"", ""topic"":""..."", ""blurb"":""..."", ""evidence"":""..."", ""confidence"":0.0 } ] }";

            var stars = TryGetStars(si.RawContent?.Content);
            var user = $@"CompanyName: {company}
                SourceType: {source}
                StarRating: {(stars is null ? "unknown" : $"{stars:0.0}")}

                Gist:
                {gist}

                GistPoints:
                - {string.Join("\n- ", points)}

                RawContent:
                {raw}";

            // --- Call OpenAI (chat/completions; no verbosity/reasoning knobs)
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg["OpenAI:ApiKey"]);
            var model = _cfg["OpenAI:ClassifierModel"] ?? _cfg["OpenAI:Model"] ?? "gpt-4o-mini";

            var body = new
            {
                model,
                messages = new[] {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                },
                max_tokens = 700
            };

            using var resp = await _http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
            var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            var cleaned = Spydomo.Utilities.JsonHelper.StripJsonCodeBlock(content);

            ReviewObsResponse? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<ReviewObsResponse>(
                    cleaned, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }

            if (parsed?.observations is null || parsed.observations.Count == 0)
                return null;

            var candidates = parsed.observations.AsEnumerable();
            if (stars is double st && st <= 3.0)
                candidates = candidates.Where(o => !o.type.Equals("Praise", StringComparison.OrdinalIgnoreCase));

            // Pick ONE to emit now: Pain > FeatureRequest > Praise
            var pick = candidates.OrderBy(o => Rank(o.type)) // lower is higher priority
                .FirstOrDefault();

            if (pick is null) return null;

            var type = pick.type;                         // "Pain" | "FeatureRequest" | "Praise"
            var topicKey = TopicKeyHelper.Slugify(pick.topic);
            var nowUtc = ctx.NowUtc;
            var today = DateOnly.FromDateTime(nowUtc);

            // 🔒 Option A: preempt ONLY for the same low-star review
            if (_opt.HeadlinePreemptsObservation && stars is double str && str <= (_opt.LowStarThreshold <= 0 ? 2.0 : _opt.LowStarThreshold))
            {
                await _obsRepo.UpsertTodayAsync(si.CompanyId, pick.type, topicKey, ctx.NowUtc, ct).ConfigureAwait(false);
                return null; // don't emit the observation for this low-star item
            }

            // Always record mention (used for surge + 14d aggregates)
            await _obsRepo.UpsertTodayAsync(si.CompanyId, type, topicKey, nowUtc, ct).ConfigureAwait(false);

            // Decide if we emit today (cooldown + surge check)
            var shouldEmit = await DedupePolicy.ShouldEmitAsync(_obsRepo, si.CompanyId, type, topicKey, nowUtc, _opt, ct).ConfigureAwait(false);
            if (!shouldEmit) return null;

            // If we emit, update LastNotifiedAt
            await _obsRepo.SetLastNotifiedAtAsync(si.CompanyId, type, topicKey, nowUtc, ct).ConfigureAwait(false);


            var tier = pick.tier switch
            {
                "Tier1" => PulseTier.Tier1,
                "Tier2" => PulseTier.Tier2,
                _ => PulseTier.Tier3
            };

            // nudge based on rating
            tier = AdjustTierByRating(pick.type, tier, stars, pick.confidence);

            var chip = pick.type switch
            {
                "Pain" => SignalSlugs.PainSignal,
                "FeatureRequest" => SignalSlugs.FeatureGap,
                _ => SignalSlugs.SocialProofDrop
            };

            return new PulsePoint(
                CompanyId: si.CompanyId,
                CompanyName: company,
                Bucket: PulseBucket.CustomerVoice,
                ChipSlug: chip,
                Tier: tier,
                Title: pick.blurb,                // already colleague-style, short
                Url: si.RawContent?.PostUrl ?? "",
                SeenAt: si.Date ?? ctx.NowUtc,
                Context: new()
                {
                    ["topic"] = pick.topic,
                    ["evidence"] = pick.evidence,
                    ["confidence"] = pick.confidence,
                    ["source"] = source,
                    ["stars"] = stars
                },
                RawContentId: si.RawContentId,
                SummarizedInfoId: si.Id
            );

            static int Rank(string type) => type switch
            {
                "Pain" => 0,
                "FeatureRequest" => 1,
                _ => 2
            };

            static List<string> ParseGistPoints(string? json)
            {
                if (string.IsNullOrWhiteSpace(json)) return new();
                try
                {
                    var arr = JsonSerializer.Deserialize<List<string>>(json);
                    return arr?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new();
                }
                catch { return new(); }
            }

            static double? TryGetStars(string? json)
            {
                if (NvarcharJson.TryGet<double>(json, "Metadata.Rating", out var rating)) return rating;
                if (NvarcharJson.TryGet<double>(json, "rating", out rating)) return rating;
                if (NvarcharJson.TryGet<double>(json, "overallRating", out rating)) return rating;
                return null;
            }
        }

        private sealed record ReviewObsOut(string type, string tier, string topic, string blurb, string evidence, double confidence);
        private sealed record ReviewObsResponse(List<ReviewObsOut> observations);

        private static PulseTier AdjustTierByRating(string type, PulseTier baseTier, double? stars, double confidence)
        {
            if (stars is null) return baseTier;

            var s = stars.Value;

            if (type.Equals("Pain", StringComparison.OrdinalIgnoreCase) && s <= 3.0)
                return PulseTier.Tier3; // don't emit praise from a low-star review

            // Pain: low stars -> promote; high stars -> demote unless very confident
            if (type.Equals("Pain", StringComparison.OrdinalIgnoreCase))
            {
                if (s <= 3.0) return PulseTier.Tier1;
                if (s >= 4.5 && confidence < 0.75)
                    return baseTier == PulseTier.Tier1 ? PulseTier.Tier2 : baseTier; // soften if model overreacted
            }

            // FeatureRequest: low stars -> promote (signals dissatisfaction/opportunity)
            if (type.Equals("FeatureRequest", StringComparison.OrdinalIgnoreCase))
            {
                if (s <= 3.0 && baseTier == PulseTier.Tier2) return PulseTier.Tier1;
            }

            // Praise: very low stars -> likely noise; keep Tier3 but you could drop entirely if you prefer
            if (type.Equals("Praise", StringComparison.OrdinalIgnoreCase))
            {
                if (s <= 3.0 && confidence < 0.7) return PulseTier.Tier3; // keep as T3 social proof (or skip)
            }

            return baseTier;
        }

    }
}
