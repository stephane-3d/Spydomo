using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Constants;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using Spydomo.Utilities;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Spydomo.Infrastructure.AiServices
{
    public class AiSummarizer : IAiSummarizer
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IAiUsageLogger _usageLogger;
        private readonly ISignalTypeOptionsProvider _signalTypeOptions;
        private readonly ILogger<AiSummarizer> _logger;

        private static readonly SemaphoreSlim _openAiThrottler = new(1, 1);
        private const int DelayPerThousandTokensMs = 1500; // Conservative throttle
        private const int MaxResponseTokens = 2000;

        public AiSummarizer(HttpClient httpClient, IConfiguration config, IDbContextFactory<SpydomoContext> dbFactory, 
            IAiUsageLogger usageLogger, ISignalTypeOptionsProvider signalTypeOptions, ILogger<AiSummarizer> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _dbFactory = dbFactory;
            _usageLogger = usageLogger;
            _signalTypeOptions = signalTypeOptions;
            _logger = logger;
        }

        public async Task<Dictionary<int, AiSummaryResult>> SummarizeBatchAsync(
            List<(int Id, string CanonicalText, OriginTypeEnum OriginType)> batch,
            int? companyId = null,
            CancellationToken ct = default)
        {
            var results = new Dictionary<int, AiSummaryResult>();
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var allowedSignals = await _signalTypeOptions.GetAllowedAsync(forceRefresh: false, ct);

            string? companyName = null;
            if (companyId != null)
            {
                var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
                companyName = company?.Name;
            }

            foreach (var group in batch.GroupBy(x => x.OriginType))
            {
                var originType = group.Key;
                var items = group.ToList();

                var groupResults = await SummarizeGroupWithSplitRetryAsync(
                    items, originType, companyName, allowedSignals, companyId, ct);

                foreach (var kv in groupResults)
                    results[kv.Key] = kv.Value;
            }

            return results;
        }

        private async Task<Dictionary<int, AiSummaryResult>> SummarizeGroupWithSplitRetryAsync(
            List<(int Id, string CanonicalText, OriginTypeEnum OriginType)> items,
            OriginTypeEnum originType,
            string? companyName,
            List<SignalTypeOption> allowedSignals,
            int? companyId,
            CancellationToken ct)
        {
            // Base case
            if (items.Count == 0)
                return new Dictionary<int, AiSummaryResult>();

            try
            {
                var prompt = BuildPrompt(items, originType, companyName ?? "", allowedSignals);
                var response = await CallOpenAiAsync(prompt, companyId, ct);

                Dictionary<int, AiSummaryResult> parsed;
                try
                {
                    parsed = ParseIndexedJsonResponse(response, allowedSignals);
                }
                catch (Exception ex) when (IsJsonParseLike(ex))
                {
                    // Treat JSON parse errors like truncation: split retry.
                    throw new AiBatchParseException("JSON parse failed", ex);
                }

                // Optional: ensure we got something for each input id (strict mode).
                // If the model returns fewer keys, treat it like a parse failure and split.
                var expectedIds = items.Select(x => x.Id).ToHashSet();
                var returnedIds = parsed.Keys.ToHashSet();

                if (!expectedIds.SetEquals(returnedIds))
                {
                    // If it's just missing a couple, you could choose to accept partial and re-run missing only.
                    // For simplicity: split retry to reduce prompt size / confusion.
                    var missing = string.Join(",", expectedIds.Except(returnedIds).Take(10));
                    throw new AiBatchParseException($"Missing IDs in response. Missing (first 10): {missing}", null);
                }

                return parsed;
            }
            catch (Exception ex) when (ShouldSplitRetry(ex) && items.Count >= 2)
            {
                _logger.LogWarning(ex,
                    "AI batch failed for OriginType={OriginType} size={Size}. Splitting and retrying.",
                    originType, items.Count);

                var mid = items.Count / 2;
                var left = items.Take(mid).ToList();
                var right = items.Skip(mid).ToList();

                var leftTask = SummarizeGroupWithSplitRetryAsync(left, originType, companyName, allowedSignals, companyId, ct);
                var rightTask = SummarizeGroupWithSplitRetryAsync(right, originType, companyName, allowedSignals, companyId, ct);

                await Task.WhenAll(leftTask, rightTask);

                var merged = new Dictionary<int, AiSummaryResult>();
                foreach (var kv in leftTask.Result) merged[kv.Key] = kv.Value;
                foreach (var kv in rightTask.Result) merged[kv.Key] = kv.Value;

                return merged;
            }
        }

        /// <summary>
        /// Only split-retry for failures that are likely caused by prompt size / formatting:
        /// - truncation (finish_reason=length) -> you throw an Exception containing "truncated" or "finish_reason=length"
        /// - empty content
        /// - JSON parse errors
        /// </summary>
        private static bool ShouldSplitRetry(Exception ex)
        {
            if (ex is AiBatchParseException) return true;

            var msg = ex.Message ?? "";

            // From CallOpenAiAsync
            if (msg.Contains("finish_reason=length", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("truncated", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("empty content", StringComparison.OrdinalIgnoreCase)) return true;

            // JsonDocument.Parse / your helpers
            if (IsJsonParseLike(ex)) return true;

            return false;
        }

        private static bool IsJsonParseLike(Exception ex)
        {
            // JsonDocument.Parse throws JsonException
            if (ex is System.Text.Json.JsonException) return true;

            // Your helper throws ArgumentException("Content is empty")
            if (ex is ArgumentException && (ex.Message?.Contains("Content is empty", StringComparison.OrdinalIgnoreCase) ?? false))
                return true;

            // Sometimes parse failures bubble as FormatException / InvalidOperationException; catch common text
            var msg = ex.Message ?? "";
            if (msg.Contains("JSON", StringComparison.OrdinalIgnoreCase) &&
                (msg.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("expected", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("parse", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private sealed class AiBatchParseException : Exception
        {
            public AiBatchParseException(string message, Exception? inner) : base(message, inner) { }
        }

        private string BuildPrompt(
            List<(int Id, string CanonicalText, OriginTypeEnum OriginType)> batch,
            OriginTypeEnum originType,
            string companyName,
            List<SignalTypeOption> allowedSignals)
        {
            var originDescription = originType == OriginTypeEnum.UserGenerated
                ? "user-generated feedback such as reviews, posts, or community comments"
                : "company-generated marketing content such as blogs, social posts, or announcements";

            var companyPhrase = string.IsNullOrWhiteSpace(companyName) ? "a product intelligence assistant" : $"a product intelligence assistant working for {companyName}";
            var prompt = new StringBuilder($@"You are {companyPhrase} to analyze multiple pieces of {originDescription} content.

For EACH record, return JSON ONLY (no markdown, no commentary). Each record must contain EXACTLY these keys:
- gist (string)
- points (array of strings, max 2)
- themes (object with up to 2 snake_case keys; each value is a neutral 8-10 word explanation; do not mention the company name)
- tags (object with up to 3 snake_case keys; optional + / - prefix; each value is a short neutral explanation)
- sentiment (object: {{ ""label"": <one of: HighlyNegative|Negative|Neutral|Positive|HighlyPositive>, ""reason"": <short> }})
- signal_types (array of objects: {{ ""id"": <number>, ""reason"": <short> }})

1) Gist: 1–2 sentences capturing the most important action/insight. Include relevant context. Avoid vague promo phrasing. Present tense. Under 50 words.
2) Points: up to 3 skimmable bullets.
3) Themes: as described above.
Example:
""themes"": {{
  ""ai_tools"": ""AI tools are helping marketers save time and improve productivity."",
  ""client_success"": ""Positive client feedback demonstrates the tool’s effectiveness."",
  ""strategic_advantage"": ""The product provides a clear edge in strategic decision-making.""
}}

4) Tags: as described above.
Example:
""tags"": {{
  ""+fast_support"": ""Support replies are prompt and helpful."",
  ""intuitive_ui"": ""The user interface is easy to understand."",
  ""-limited_integrations"": ""Some key third-party tools are missing from the integrations list.""
}}

5) Sentiment: choose label + reason.

6) Strategic Signal Types: Based on the gist, themes, tags and tone, return a list of signals.
Choose only from this fixed list and return IDs only.

Allowed signal types (return IDs only):");


            foreach (var s in allowedSignals)
                prompt.AppendLine($"- {s.Id} — {s.Name} — {s.Description}");

            prompt.AppendLine(@"
For each signal you detect, return an `id` and a short `reason`.

Return in this format:
""signal_types"": [
  {
    ""id"": <signalTypeId>,
    ""reason"": ""<a short reason>""
  }
]");


            prompt.AppendLine(@"
Signal type output rules:
- signal_types must be an array of objects with ONLY: { ""id"": number, ""reason"": string }
- Do NOT output label/name/slug.
- If none apply, return ""signal_types"": [].

IMPORTANT:
- Output MUST be a single JSON object and MUST start with '{' and end with '}'.
- Do NOT wrap in ``` or any code block.
- Do NOT output whitespace-only.

Return a valid JSON with this structure:
{
  ""3912"": { ... },
  ""4021"": { ... }
}

Here are the records:");

            foreach (var (id, text, _) in batch)
            {
                prompt.AppendLine($"\nRecord ID {id}:\n---\n{text}\n");
            }


            return prompt.ToString();
        }

        private async Task<string> CallOpenAiAsync(string prompt, int? companyId = null, CancellationToken ct = default)
        {
            int estimatedPromptTokens = EstimateTokenCount(prompt);
            int estimatedTotalTokens = estimatedPromptTokens + MaxResponseTokens;

            var apiKey = _config["OpenAI:ApiKey"];
            var gptModel = _config["OpenAI:Model"];

            await _openAiThrottler.WaitAsync(ct);

            try
            {
                var requestBody = new
                {
                    model = gptModel,
                    messages = new[] { new { role = "user", content = prompt } },
                    max_completion_tokens = MaxResponseTokens,
                    reasoning_effort = "minimal",
                    verbosity = "low"
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                req.Content = JsonContent.Create(requestBody);

                var response = await _httpClient.SendAsync(req, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    throw new Exception($"OpenAI API call failed: {error}");
                }

                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

                await _usageLogger.LogAsync(json, "GistExtraction", companyId, prompt);

                var choice0 = json.GetProperty("choices")[0];
                var finishReason = choice0.TryGetProperty("finish_reason", out var fr)
                    ? (fr.GetString() ?? "")
                    : "";

                // If truncated, fail fast so caller can shrink batch.
                if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "OpenAI response truncated (finish_reason=length). companyId={CompanyId} promptTokens≈{PromptTokens} maxCompletionTokens={MaxTokens}",
                        companyId, estimatedPromptTokens, MaxResponseTokens);

                    throw new Exception("OpenAI response truncated (finish_reason=length). Reduce batch size or output size.");
                }

                // Optional: log usage tokens when available
                if (json.TryGetProperty("usage", out var usage))
                {
                    int? pt = usage.TryGetProperty("prompt_tokens", out var pte) && pte.ValueKind == JsonValueKind.Number ? pte.GetInt32() : null;
                    int? ctok = usage.TryGetProperty("completion_tokens", out var cte) && cte.ValueKind == JsonValueKind.Number ? cte.GetInt32() : null;
                    int? tt = usage.TryGetProperty("total_tokens", out var tte) && tte.ValueKind == JsonValueKind.Number ? tte.GetInt32() : null;

                    _logger.LogInformation("OpenAI usage: prompt={Prompt} completion={Completion} total={Total} finish_reason={FinishReason}",
                        pt, ctok, tt, finishReason);
                }

                var text = ExtractAssistantText(json);

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("OpenAI returned empty content. finish_reason={FinishReason} companyId={CompanyId}", finishReason, companyId);
                    throw new Exception($"OpenAI returned empty content. finish_reason={finishReason}");
                }

                return text;
            }
            finally
            {
                // Apply delay based on total estimated tokens
                int delay = (int)Math.Ceiling((estimatedTotalTokens / 1000.0) * DelayPerThousandTokensMs);

                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
                _openAiThrottler.Release();
            }
        }

        private static int EstimateTokenCount(string text)
        {
            // Approximation: 1 token ≈ 4 characters for English text
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        private Dictionary<int, AiSummaryResult> ParseIndexedJsonResponse(
            string content,
            List<SignalTypeOption> allowedSignals)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new Dictionary<int, AiSummaryResult>();

            var cleanJson = JsonHelper.StripJsonCodeBlock(content);
            var parsed = JsonDocument.Parse(cleanJson);
            var result = new Dictionary<int, AiSummaryResult>();

            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                if (int.TryParse(property.Name, out int id))
                {
                    var item = property.Value;

                    string categoryLabel = "";
                    string categoryReason = "";
                    if (item.TryGetProperty("category", out var categoryElement))
                    {
                        if (categoryElement.ValueKind == JsonValueKind.Object)
                        {
                            categoryLabel = categoryElement.GetProperty("label").GetString() ?? "";
                            categoryReason = categoryElement.GetProperty("reason").GetString() ?? "";
                        }
                        else if (categoryElement.ValueKind == JsonValueKind.String)
                        {
                            categoryLabel = categoryElement.GetString() ?? "";
                        }
                    }

                    string sentimentLabel = "";
                    string sentimentReason = "";
                    if (item.TryGetProperty("sentiment", out var sentimentElement))
                    {
                        if (sentimentElement.ValueKind == JsonValueKind.Object)
                        {
                            sentimentLabel = sentimentElement.GetProperty("label").GetString() ?? "";
                            sentimentReason = sentimentElement.GetProperty("reason").GetString() ?? "";
                        }
                        else if (sentimentElement.ValueKind == JsonValueKind.String)
                        {
                            sentimentLabel = sentimentElement.GetString() ?? "";
                        }
                    }

                    var signalTypes = new List<(int SignalTypeId, string Reason)>();
                    if (item.TryGetProperty("signal_types", out var signalTypesElement) &&
                        signalTypesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sig in signalTypesElement.EnumerateArray())
                        {
                            int stid = 0;

                            if (sig.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                                stid = idEl.GetInt32();

                            var reason = sig.TryGetProperty("reason", out var rEl) ? (rEl.GetString() ?? "") : "";

                            if (stid > 0)
                                signalTypes.Add((stid, reason));
                        }
                    }

                    var allowedIds = allowedSignals.Select(x => x.Id).ToHashSet();

                    signalTypes = signalTypes
                        .Where(x => allowedIds.Contains(x.SignalTypeId))
                        .DistinctBy(x => x.SignalTypeId)
                        .ToList();

                    var summary = new AiSummaryResult
                    {
                        Gist = item.GetProperty("gist").GetString() ?? "",
                        Points = item.GetProperty("points").EnumerateArray().Select(p => p.GetString() ?? "").ToList(),
                        Themes = JsonHelper.ParseKeyValueObject(item.GetProperty("themes")),
                        Tags = JsonHelper.ParseKeyValueObject(item.GetProperty("tags")),
                        Category = (categoryLabel, categoryReason),
                        Sentiment = (sentimentLabel, sentimentReason),
                        SignalTypes = signalTypes
                    };

                    result[id] = summary;
                }
            }

            return result;
        }

        private static string ExtractAssistantText(JsonElement root)
        {
            var choice0 = root.GetProperty("choices")[0];

            // Diagnostics
            string finishReason = choice0.TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? "") : "";

            var message = choice0.GetProperty("message");

            // Some responses can include a refusal field
            if (message.TryGetProperty("refusal", out var refusalEl) && refusalEl.ValueKind == JsonValueKind.String)
            {
                var refusal = refusalEl.GetString();
                if (!string.IsNullOrWhiteSpace(refusal))
                    return ""; // keep empty -> caller handles with better error
            }

            if (!message.TryGetProperty("content", out var contentEl))
                return "";

            // Most common: string content
            if (contentEl.ValueKind == JsonValueKind.String)
                return contentEl.GetString() ?? "";

            // Sometimes: array of content parts [{type:"text", text:"..."}, ...]
            if (contentEl.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in contentEl.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object) continue;

                    if (part.TryGetProperty("type", out var typeEl) &&
                        typeEl.ValueKind == JsonValueKind.String &&
                        typeEl.GetString() == "text" &&
                        part.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(textEl.GetString());
                    }
                }
                return sb.ToString();
            }

            return "";
        }
    }

}
