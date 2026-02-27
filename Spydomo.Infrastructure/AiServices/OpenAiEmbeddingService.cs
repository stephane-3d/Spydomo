using Microsoft.Extensions.Configuration;
using Spydomo.Infrastructure.Interfaces;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public class OpenAiEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IAiUsageLogger _usageLogger;

        public OpenAiEmbeddingService(HttpClient httpClient, IConfiguration config, IAiUsageLogger usageLogger)
        {
            _httpClient = httpClient;
            _config = config;
            _usageLogger = usageLogger;
        }

        public async Task<List<float>> GetEmbeddingAsync(string text, int? companyId = null)
        {
            var requestBody = new
            {
                input = text,
                model = _config["OpenAI:EmbeddingModel"]
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
            request.Headers.Add("Authorization", $"Bearer {_config["OpenAI:ApiKey"]}");
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var parsed = await response.Content.ReadFromJsonAsync<JsonElement>();
            var embeddingArray = parsed
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToList();

            // Log usage 
            if (_usageLogger != null)
            {
                await _usageLogger.LogAsync(parsed, "Embedding Extraction", companyId);
            }

            return embeddingArray;
        }

        // ✅ LLM judge for ambiguous embedding matches (Responses API)
        public async Task<ThemeJudgeResult> JudgeThemeMatchAsync(
            string rawTheme,
            string? reason,
            IReadOnlyList<ThemeJudgeCandidate> candidates,
            int? companyId = null,
            CancellationToken ct = default)
        {
            var model = _config["OpenAI:JudgeModel"]; // e.g. "gpt-4o-mini" or whatever you configure

            var system = """
You are a strict taxonomy normalizer for product feedback themes.

Given:
- raw theme (name + meaning)
- candidate canonical themes, each with:
  - id
  - name
  - definition (authoritative meaning)

Decide if the raw theme is the SAME concept as one candidate.

Rules:
- Only match if it is clearly the same concept, not just related.
- If the raw theme is broader than a candidate, choose "new" unless the candidate definition is equally broad.
- Prefer "new" if uncertain.
- Use the candidate definitions heavily.

Return JSON ONLY:
{
  "decision": "match" | "new",
  "bestId": number | null,
  "confidence": number,
  "rationale": string
}
""";


            var userObj = new
            {
                rawTheme,
                reason,
                candidates
            };

            // Responses API payload (official endpoint /v1/responses) :contentReference[oaicite:3]{index=3}
            var payload = new
            {
                model,
                input = new object[]
                {
                new {
                    role = "system",
                    content = new object[] { new { type = "text", text = system } }
                },
                new {
                    role = "user",
                    content = new object[] { new { type = "text", text = JsonSerializer.Serialize(userObj) } }
                }
                },
                temperature = 0,
                max_output_tokens = 250
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config["OpenAI:ApiKey"]);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var parsed = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Log usage (you may want to log "Judge Theme Match" separately)
            await _usageLogger.LogAsync(parsed, "Theme Match Judge", companyId);

            // Extract text output (robust-ish)
            var text = ExtractResponseText(parsed);
            return JsonSerializer.Deserialize<ThemeJudgeResult>(text)
                   ?? new ThemeJudgeResult("new", null, 0.0, "Failed to parse judge output");
        }

        private static string ExtractResponseText(JsonElement parsed)
        {
            // Responses API returns output[] content blocks; structure can vary by SDK/version.
            // This tries common patterns.
            if (parsed.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
                return ot.GetString() ?? "";

            if (parsed.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var msg in output.EnumerateArray())
                {
                    if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in content.EnumerateArray())
                        {
                            if (c.TryGetProperty("type", out var type) &&
                                type.ValueKind == JsonValueKind.String &&
                                type.GetString() == "output_text" &&
                                c.TryGetProperty("text", out var t) &&
                                t.ValueKind == JsonValueKind.String)
                            {
                                return t.GetString() ?? "";
                            }
                        }
                    }
                }
            }

            return "";
        }

        public record ThemeJudgeCandidate(int Id, string Name, string Definition);

        public record ThemeJudgeResult(string decision, int? bestId, double confidence, string rationale);
    }

}
