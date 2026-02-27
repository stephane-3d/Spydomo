using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spydomo.Common.Constants;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.BackgroundServices;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Net.Http.Json;
using System.Text.Json;

namespace Spydomo.Infrastructure.PulseRules.CompanyContent
{
    // Detects product launches, partnerships, funding, and leadership changes in company-authored content.
    [RuleMeta(Order = 20)]
    public sealed class CompanyObservationRule : ICompanyContentRule
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _cfg;
        private readonly PulseRulesOptions _opt;
        private readonly IPulseObservationRepository _obsRepo;
        private readonly ILogger<StrategicSummaryService> _logger;

        public CompanyObservationRule(HttpClient http, IConfiguration cfg, IOptions<PulseRulesOptions> opt, IPulseObservationRepository obsRepo, ILogger<StrategicSummaryService> logger)
        {
            _http = http;
            _cfg = cfg;
            _opt = opt.Value;
            _obsRepo = obsRepo;
            _logger = logger;
        }

        public bool IsMatch(SummarizedInfo si, TrackContext ctx)
            => PulseUtils.IsContentSource(si.SourceTypeEnum);

        public async Task<PulsePoint?> ProjectAsync(SummarizedInfo si, TrackContext ctx, CancellationToken ct = default)
        {
            var company = si.Company?.Name ?? "Unknown";
            var source = si.SourceType?.Name ?? "Unknown";
            var gist = si.Gist ?? "";
            var points = ParseGistPoints(si.GistPointsJson);
            var raw = si.RawContent?.Content ?? "";

            var siId = si.Id;
            var companyId = si.CompanyId;
            var src = si.SourceTypeEnum.ToString();
            var rawId = si.RawContentId;

            _logger.LogInformation("CompanyObs START siId={SiId} companyId={CompanyId} src={Src} rawId={RawId}",
                siId, companyId, src, rawId);

            // (optional) skip if gist is missing so you reduce calls
            if (string.IsNullOrWhiteSpace(si.Gist))
            {
                _logger.LogInformation("CompanyObs SKIP siId={SiId} reason=NoGist", siId);
                return null;
            }

            // --- Build prompt
            var system = @"You are a competitive intelligence formatter.

                You will receive one company-generated content item with:
                - Gist (1–2 sentences summarizing the content)
                - GistPoints (key extracted bullets)
                - RawContent (short text or post excerpt)

                Identify 0–3 strategic observations that reveal meaningful company actions.

                Possible observation types:
                - FeatureLaunch → Product launches, updates, integrations, or new features.
                - StrategicMove → Partnerships, funding rounds, acquisitions, or leadership hires.
                - MarketRecognition → Awards, analyst mentions, or recognitions.

                For EACH observation return:
                - signalType: FeatureLaunch | StrategicMove | MarketRecognition
                - headline: concise headline (~12 words)
                - description: 1–2 sentence context of what happened and why it matters
                - tier: Tier1 (major) | Tier2 (moderate) | Tier3 (minor)
                - confidence: 0..1

                Output strict JSON:
                { ""observations"": [ { ""signalType"":""FeatureLaunch|StrategicMove|MarketRecognition"", ""headline"":""..."", ""description"":""..."", ""tier"":""Tier1|Tier2|Tier3"", ""confidence"":0.0 } ] }";

            var user = $@"CompanyName: {company}
                SourceType: {source}

                Gist:
                {gist}

                GistPoints:
                - {string.Join("\n- ", points)}

                RawContent:
                {raw}";

            // --- Call OpenAI
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg["OpenAI:ApiKey"]);
            var model = _cfg["OpenAI:ClassifierModel"] ?? _cfg["OpenAI:Model"] ?? "gpt-4o-mini";

            var body = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                },
                max_tokens = 700
            };

            using var resp = await _http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
            var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            var cleaned = JsonHelper.StripJsonCodeBlock(content);

            CompanyObsResponse? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<CompanyObsResponse>(
                    cleaned, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }

            if (parsed?.observations is null || parsed.observations.Count == 0)
                return null;

            // Pick first major one (Tier1 > Tier2 > Tier3)
            var pick = parsed.observations.OrderBy(o => Rank(o.tier)).FirstOrDefault();
            if (pick is null) return null;

            // Deduping & repository logging
            var typeKey = TopicKeyHelper.Slugify(pick.signalType + ":" + pick.headline);
            var nowUtc = ctx.NowUtc;

            await _obsRepo.UpsertTodayAsync(si.CompanyId, pick.signalType, typeKey, nowUtc, ct).ConfigureAwait(false);
            var shouldEmit = await DedupePolicy.ShouldEmitAsync(_obsRepo, si.CompanyId, pick.signalType, typeKey, nowUtc, _opt, ct).ConfigureAwait(false);
            if (!shouldEmit) return null;

            await _obsRepo.SetLastNotifiedAtAsync(si.CompanyId, pick.signalType, typeKey, nowUtc, ct).ConfigureAwait(false);

            var tier = pick.tier switch
            {
                "Tier1" => PulseTier.Tier1,
                "Tier2" => PulseTier.Tier2,
                _ => PulseTier.Tier3
            };

            var chip = pick.signalType switch
            {
                "FeatureLaunch" => SignalSlugs.FeatureLaunch,
                "StrategicMove" => SignalSlugs.StrategicMove,
                "MarketRecognition" => SignalSlugs.SocialProofDrop,
                _ => SignalSlugs.StrategicMove
            };

            return new PulsePoint(
                CompanyId: si.CompanyId,
                CompanyName: company,
                Bucket: PulseBucket.CompanyActivity,
                ChipSlug: chip,
                Tier: tier,
                Title: pick.headline,
                Url: si.RawContent?.PostUrl ?? "",
                SeenAt: si.Date ?? ctx.NowUtc,
                Context: new()
                {
                    ["description"] = pick.description,
                    ["confidence"] = pick.confidence,
                    ["source"] = source
                },
                RawContentId: si.RawContentId,
                SummarizedInfoId: si.Id
            );
        }

        private sealed record CompanyObsOut(string signalType, string headline, string description, string tier, double confidence);
        private sealed record CompanyObsResponse(List<CompanyObsOut> observations);

        private static int Rank(string tier) => tier switch
        {
            "Tier1" => 0,
            "Tier2" => 1,
            _ => 2
        };

        private static List<string> ParseGistPoints(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(json);
                return arr?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new();
            }
            catch { return new(); }
        }
    }
}
