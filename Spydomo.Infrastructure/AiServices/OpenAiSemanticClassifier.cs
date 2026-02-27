using Microsoft.Extensions.Configuration;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Spydomo.Infrastructure.AiServices
{
    public sealed class OpenAiSemanticClassifier : ISemanticClassifier
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _cfg;
        private readonly ISemanticSignalRepository _repo;

        public OpenAiSemanticClassifier(HttpClient http, IConfiguration cfg, ISemanticSignalRepository repo)
        {
            _http = http; _cfg = cfg; _repo = repo;
        }

        public async Task<IntentResult> ClassifyAsync(TextSample s, CancellationToken ct = default)
        {
            // 1) Cache by content hash
            var hash = HashKey(s.SourceType, s.CompanyId, s.Text);
            var cached = await _repo.GetByHashAsync(hash, ct);
            if (cached is not null)
                return ToResult(cached);

            // 2) Call OpenAI (few-shot prompt kept simple here)
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg["OpenAI:ApiKey"]);

            var model = _cfg["OpenAI:ClassifierModel"]
                         ?? _cfg["OpenAI:Model"]
                         ?? "gpt-4o-mini";

            var system = "Classify text into intents: SwitchingIntent, ComparisonIntent, FeatureRequest, PricingComplaint, PerformanceIssue, SupportIssue, MigrationIntent, NewVertical, None. Return strict JSON: { \"lang\": \"en\", \"intents\": [{\"name\":\"...\",\"confidence\":0.0}], \"keywords\": [\"...\"] }";
            var body = new
            {
                model,
                messages = new[] {
                    new { role = "system", content = system },
                    new { role = "user", content = s.Text }
                },
                max_completion_tokens = 220
            };

            var resp = await _http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body, ct);
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            var cleaned = JsonHelper.StripJsonCodeBlock(content ?? "{}");

            var parsed = JsonSerializer.Deserialize<LLMOut>(cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // 3) Extract keywords locally too (cheap)
            var kwLocal = KeywordExtractor.ExtractKeywords(s.Text, parsed.Lang ?? "und");
            var keywords = (parsed.Keywords is { Count: > 0 }) ? parsed.Keywords! : kwLocal;

            // 4) Persist
            var intentsForStorage = (parsed.Intents ?? new())
                .Select(i => new IntentHit(
                    Name: ParseIntent(i.Name),         // string -> enum
                    Confidence: Clamp(i.Confidence)))
                .ToList();

            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() } // enums as strings in JSON
            };

            var row = new SemanticSignal
            {
                CompanyId = s.CompanyId,
                SourceType = s.SourceType,
                RawContentId = s.RawContentId,
                SummarizedInfoId = s.SummarizedInfoId,
                SeenAt = s.SeenAt,
                Lang = parsed.Lang ?? "und",
                Classifier = "llm-v1",
                IntentsJson = JsonSerializer.Serialize(intentsForStorage, jsonOpts), // <-- store enum-based DTO
                KeywordsJson = JsonSerializer.Serialize(keywords, jsonOpts),
                Embedding = null,
                ModelScore = intentsForStorage.Select(h => h.Confidence).DefaultIfEmpty(0).Max(),
                Hash = hash
            };
            await _repo.UpsertAsync(row, ct);

            return ToResult(row);

            static JsonSerializerOptions DefaultJsonOpts() => new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

            static IntentResult ToResult(SemanticSignal ss)
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                // Read enum-based DTO directly
                var stored = JsonSerializer.Deserialize<List<IntentHit>>(ss.IntentsJson ?? "[]", opts) ?? new();
                // Map to runtime result type
                var hits = stored.Select(i => new IntentHit(i.Name, i.Confidence)).ToList();
                return new IntentResult(ss.Lang, hits);
            }

            static Intent ParseIntent(string? s) =>
                Enum.TryParse<Intent>(s ?? "", true, out var v) ? v : Intent.None;

            static double Clamp(double d) => d < 0 ? 0 : (d > 1 ? 1 : d);
        }

        private static string HashKey(string source, int companyId, string text)
            => Hashing.Sha256Hex($"{source}|{companyId}|{Canonicalize(text)}");

        private static string Canonicalize(string text)
        {
            var t = text?.Trim() ?? "";
            t = Regex.Replace(t, @"https?://\S+", "");  // drop URLs
            t = Regex.Replace(t, @"\s+", " ");
            return t.ToLowerInvariant();
        }

        // Local JSON DTOs for model output
        private sealed class LLMOut
        {
            public string? Lang { get; set; }
            public List<LLMHit>? Intents { get; set; } = new();
            public List<string>? Keywords { get; set; }
        }
        private sealed class LLMHit
        {
            public string? Name { get; set; }
            public double Confidence { get; set; }
        }
    }
}
