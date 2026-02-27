using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Utilities;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spydomo.Infrastructure.AiServices
{
    public sealed class OpenAiPulseAgent : IPulseAgent
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<OpenAiPulseAgent> _logger;
        private readonly IAiUsageLogger _usageLogger;

        public OpenAiPulseAgent(
            HttpClient httpClient,
            IConfiguration config,
            ILogger<OpenAiPulseAgent> logger,
            IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
            _usageLogger = usageLogger;
        }

        public async Task<IReadOnlyList<PulseBlurb>> GeneratePulsesAsync(
    PulseAgentContext context,
    CancellationToken ct = default)
        {
            var opId = Guid.NewGuid().ToString("N")[..8];
            var sw = Stopwatch.StartNew();

            var points = context.CandidatePulsePoints ?? Array.Empty<PulsePoint>();
            if (!points.Any())
                return Array.Empty<PulseBlurb>();

            _logger.LogInformation("[PA:{Op}] START groupId={GroupId} points={Count} byBucket={ByBucket}",
                opId,
                context.GroupId,
                points.Count,
                string.Join(", ", points.GroupBy(x => x.Bucket).Select(g => $"{g.Key}:{g.Count()}")));

            var apiKey = _config["OpenAI:ApiKey"];
            var gptModel = _config["OpenAI:PulseAgentModel"] ?? "gpt-4o-mini";

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var results = new List<PulseBlurb>();

            // Group by Bucket – still useful for specialized instructions
            foreach (var grp in points.GroupBy(p => p.Bucket))
            {
                var bucket = grp.Key;
                var items = grp.ToList();

                var system = bucket switch
                {
                    PulseBucket.CustomerVoice => @"You are a competitive intelligence analyst focused on customer voice (reviews, forums, feedback).

Your goal is to curate meaningful pulses for a SaaS leadership team:
- Focus on clear patterns of pain, praise, feature requests, or sentiment shifts.
- Prefer bigger trends over one-off anecdotes.
- You will receive candidate signals extracted by deterministic rules; treat them as suggestions, not obligations.",
                    PulseBucket.Marketing => @"You are a competitive intelligence analyst focused on marketing signals (copy, ads, content, campaigns).

Your goal is to surface shifts in positioning, messaging, channels, or themes that matter strategically.",
                    PulseBucket.Product => @"You are a competitive intelligence analyst focused on product/pricing/integrations.

Your goal is to surface changes that affect value, lock-in, upsell, or platform direction.",
                    _ => @"You are a competitive intelligence analyst curating pulses from mixed signals."
                };

                // What we ask the model to return (more agent-y):
                var user = BuildAgentUserMessageForBucket(context, bucket, items);

                var body = new
                {
                    model = gptModel,
                    messages = new[]
                    {
                        new { role = "system", content = system },
                        new { role = "user",   content = user }
                    },
                    max_completion_tokens = 1600,
                    reasoning_effort = "minimal"
                };

                _logger.LogInformation("[PA:{Op}] CALL groupId={GroupId} bucket={Bucket} items={Items} promptChars(system,user)={SysChars},{UserChars} elapsedMs={Ms}",
                    opId,
                    context.GroupId,
                    bucket,
                    items.Count,
                    system?.Length ?? 0,
                    user?.Length ?? 0,
                    sw.ElapsedMilliseconds);

                var bucketSw = Stopwatch.StartNew();

                try
                {
                    using var resp = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body, ct);
                    var respText = await resp.Content.ReadAsStringAsync(ct);

                    _logger.LogInformation("[PA:{Op}] RESP bucket={Bucket} status={Status} bytes={Bytes} elapsedMs={Ms}",
                        opId, bucket, (int)resp.StatusCode, respText?.Length ?? 0, bucketSw.ElapsedMilliseconds);

                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogError("[PA:{Op}] OpenAI ERROR bucket={Bucket} status={StatusCode} bodyHead={BodyHead}",
                            opId, bucket, resp.StatusCode, (respText?.Length ?? 0) > 400 ? respText![..400] : respText);

                        // full fallback: use titles as-is for this bucket
                        results.AddRange(FallbackBlurbs(items));
                        continue;
                    }

                    var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                    var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";

                    _logger.LogInformation("[PA:{Op}] CONTENT bucket={Bucket} contentChars={Chars}",
                        opId, bucket, content?.Length ?? 0);

                    await _usageLogger.LogAsync(json, $"PulseAgent:{bucket} for GroupId", context.GroupId, system + "\n\n" + user);

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogInformation("[PA:{Op}] EMPTY bucket={Bucket} items={Count}",
                            opId, bucket, items.Count);
                        continue; // no pulses for this bucket
                    }

                    var cleaned = JsonHelper.StripJsonCodeBlock(content);

                    List<PulseAgentOut> parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<List<PulseAgentOut>>(
                            cleaned,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        ) ?? new();
                    }
                    catch (Exception jex)
                    {
                        _logger.LogError(jex, "[PA:{Op}] JSON FAIL bucket={Bucket} cleanedHead={Head}",
                            opId, bucket, cleaned.Length > 400 ? cleaned[..400] : cleaned);

                        results.AddRange(FallbackBlurbs(items));
                        continue;
                    }

                    _logger.LogInformation("[PA:{Op}] PARSED bucket={Bucket} parsedCount={Count}",
                        opId, bucket, parsed.Count);

                    // Map back to PulseBlurb (original logic)
                    var bySi = items
                        .Where(p => p.SummarizedInfoId.HasValue)
                        .GroupBy(p => (p.CompanyId, p.SummarizedInfoId!.Value))
                        .ToDictionary(g => g.Key, g => g.First());

                    var byRaw = items
                        .Where(p => p.RawContentId.HasValue)
                        .GroupBy(p => (p.CompanyId, p.RawContentId!.Value))
                        .ToDictionary(g => g.Key, g => g.First());

                    var byTitle = items
                        .GroupBy(p => (p.CompanyId, TitleKey(p.Title)))
                        .ToDictionary(g => g.Key, g => g.First());

                    foreach (var o in parsed)
                    {
                        PulsePoint? match = null;

                        if (o.CompanyId is int cid && cid > 0)
                        {
                            if (o.SummarizedInfoId is int si && bySi.TryGetValue((cid, si), out var m1)) match = m1;
                            else if (o.RawContentId is int rid && byRaw.TryGetValue((cid, rid), out var m2)) match = m2;
                            else if (!string.IsNullOrWhiteSpace(o.Title) && byTitle.TryGetValue((cid, TitleKey(o.Title)), out var m3)) match = m3;
                        }

                        // final fallback: try title match across any company (rarely needed but helps when model omits companyId)
                        match ??= !string.IsNullOrWhiteSpace(o.Title)
                            ? items.FirstOrDefault(p => TitleKey(p.Title) == TitleKey(o.Title))
                            : null;

                        if (match is null)
                            continue;

                        var tier = match.Tier;
                        if (!string.IsNullOrWhiteSpace(o.Tier))
                        {
                            var normalizedTier = o.Tier!.Replace(" ", "", StringComparison.OrdinalIgnoreCase).Trim();
                            if (Enum.TryParse<PulseTier>(normalizedTier, ignoreCase: true, out var parsedTier))
                                tier = parsedTier;
                        }

                        // IMPORTANT: tierReason should not be empty (your prompt demands it)
                        var tierReason = !string.IsNullOrWhiteSpace(o.TierReason)
                            ? o.TierReason.Trim()
                            : "Curated by agent from this period’s candidate signals.";

                        results.Add(new PulseBlurb(
                            match.CompanyId,
                            match.CompanyName,
                            string.IsNullOrWhiteSpace(o.Blurb) ? match.Title : o.Blurb.Trim(),
                            tier,
                            tierReason,
                            match.RawContentId,
                            match.SummarizedInfoId,
                            match.Url,
                            match.ChipSlug,
                            match.Bucket,
                            match.SourceKey
                        ));
                    }

                    _logger.LogInformation("[PA:{Op}] MAPPED bucket={Bucket} blurbsAdded={Count}", opId, bucket, results.Count);

                    static string TitleKey(string s)
                        => (s ?? "").Trim().ToLowerInvariant();

                    // Optional: if the model returns nothing for this bucket, keep a cheap fallback
                    if (!parsed.Any())
                    {
                        results.AddRange(FallbackBlurbs(items));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PA:{Op}] FAIL bucket={Bucket} elapsedMs={Ms}",
                        opId, bucket, bucketSw.ElapsedMilliseconds);

                    results.AddRange(FallbackBlurbs(items));
                }
            }

            _logger.LogInformation("[PA:{Op}] DONE groupId={GroupId} blurbs={Count} elapsedMs={Ms}",
                opId, context.GroupId, results.Count, sw.ElapsedMilliseconds);

            return results;
        }


        private static IEnumerable<PulseBlurb> FallbackBlurbs(IEnumerable<PulsePoint> items)
            => items.Select(p => new PulseBlurb(
                p.CompanyId,
                p.CompanyName,
                p.Title,
                p.Tier,
                "Fallback from rules; agent returned no curated pulses.",
                p.RawContentId,
                p.SummarizedInfoId,
                p.Url,
                p.ChipSlug,
                p.Bucket,
                p.SourceKey
            ));

        private static string BuildAgentUserMessageForBucket(
            PulseAgentContext ctx,
            PulseBucket bucket,
            IReadOnlyList<PulsePoint> items)
        {
            var sb = new StringBuilder();

            // High-level role + context
            // High-level role + context
            sb.AppendLine("You are a competitive intelligence analyst for SaaS companies.");

            sb.AppendLine(bucket switch
            {
                PulseBucket.CustomerVoice =>
                    "You focus on customer voice: reviews, forums, user feedback.",
                PulseBucket.Marketing =>
                    "You focus on marketing signals: positioning, messaging, campaigns, channels, and content strategy.",
                PulseBucket.Product =>
                    "You focus on product signals: pricing, packaging, integrations, platform direction, and release momentum.",
                _ =>
                    "You focus on strategic signals across marketing, product, and customer voice."
            });

            sb.AppendLine("Your goal is to surface a few high-value signals a VP of Product/Marketing would care about.");
            sb.AppendLine();

            sb.AppendLine();
            sb.AppendLine("Context:");
            sb.AppendLine($"- GroupId: {ctx.GroupId}");
            sb.AppendLine($"- Period: {ctx.PeriodStartUtc:yyyy-MM-dd} to {ctx.PeriodEndUtc:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine("You will receive CANDIDATE signals extracted by deterministic rules plus high-level metrics.");
            sb.AppendLine("Treat candidates as suggestions, not obligations.");
            sb.AppendLine("Your job:");
            sb.AppendLine("- Select only the most relevant signals (you may drop weak or redundant ones).");
            sb.AppendLine("- You may merge near-duplicates into a single stronger pulse.");
            sb.AppendLine("- You may adjust tiers when appropriate (Tier1 = strategically important, Tier3 = minor but noteworthy).");
            sb.AppendLine();

            // Output schema
            sb.AppendLine();
            sb.AppendLine("Return a JSON array, where each object is:");
            sb.AppendLine(@"{ 
              ""companyId"": <int>, 
              ""companyName"": ""..."", 
              ""title"": ""<original title you used as reference or a short label>"",
              ""blurb"": ""<one-line colleague-style summary, ≤ 24 words>"",
              ""tier"": ""Tier1|Tier2|Tier3"",
              ""tierReason"": ""<short justification, ≤ 20 words>"",
              ""rawContentId"": <int|null>,
              ""summarizedInfoId"": <int|null>
            }");
            sb.AppendLine();

            // Style & constraints
            sb.AppendLine("Blurb style:");
            sb.AppendLine("- Exactly ONE sentence, ≤ 24 words.");
            sb.AppendLine("- Ground it in concrete pain, benefit, or change (not generic praise).");
            sb.AppendLine("- Avoid exaggeration. If you are not sure volume is large, say 'some users' instead of 'users'.");
            sb.AppendLine();

            sb.AppendLine("Tier & tierReason rules:");
            sb.AppendLine("- Tier1: major risk/opportunity, repeated pain, or strong thematic shift.");
            sb.AppendLine("- Tier2: clear signal worth tracking, but not yet a major headwind/tailwind.");
            sb.AppendLine("- Tier3: minor but interesting, or early weak signal.");
            sb.AppendLine("- tierReason is REQUIRED and must NEVER be empty.");
            sb.AppendLine("- In tierReason, mention the main driver (e.g., repeated theme, low stars, strong surge).");
            sb.AppendLine("- When helpful, reference metrics/themes briefly, e.g. 'integration_challenges mentioned several times this period vs none before'.");
            sb.AppendLine();

            // 2) Candidate pulse points
            sb.AppendLine("Candidates (each is a suggested signal from rules; you do NOT need to keep them all):");

            var jsonOpts = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            var idx = 1;
            foreach (var it in items)
            {
                var obj = new
                {
                    it.CompanyId,
                    it.CompanyName,
                    Bucket = it.Bucket.ToString(),
                    ChipSlug = it.ChipSlug,   // SignalTypeEnum as string (hint only)
                    Tier = it.Tier.ToString(),
                    Title = it.Title,
                    it.Url,
                    it.Context,
                    it.RawContentId,
                    it.SummarizedInfoId
                };

                sb.AppendLine($"#{idx++}: " + JsonSerializer.Serialize(obj, jsonOpts));
            }

            sb.AppendLine();
            sb.AppendLine("Important:");
            sb.AppendLine("- If there is at least one candidate, return at least one curated pulse (even if you merge several into one).");
            sb.AppendLine("- tierReason is REQUIRED and must NEVER be empty; keep it concise and concrete.");
            sb.AppendLine("- Return ONLY the final curated pulses, NOT every candidate.");
            sb.AppendLine("- When in doubt, prefer fewer, higher-quality pulses.");

            return sb.ToString();
        }


        private sealed class PulseAgentOut
        {
            public int? CompanyId { get; set; }
            public string? CompanyName { get; set; }
            public string? Title { get; set; }
            public string? Blurb { get; set; }
            public string? Tier { get; set; }
            public string? TierReason { get; set; }
            public string? Chip { get; set; }
            public int? RawContentId { get; set; }
            public int? SummarizedInfoId { get; set; }
        }

    }

}
