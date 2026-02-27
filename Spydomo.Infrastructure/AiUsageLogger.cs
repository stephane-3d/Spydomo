using Microsoft.EntityFrameworkCore;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure
{
    public class AiUsageLogger : IAiUsageLogger
    {
        private readonly IDbContextFactory<SpydomoContext> _dbContextFactory;

        public AiUsageLogger(IDbContextFactory<SpydomoContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task LogAsync(JsonDocument responseJson, string purpose, int? companyId = null, string? prompt = null)
        {
            var root = responseJson.RootElement;
            var model = root.TryGetProperty("model", out var mp) ? (mp.GetString() ?? "unknown") : "unknown";

            int inputTokens = 0;
            int outputTokens = 0;
            int cachedInputTokens = 0;

            double? providerReportedCostUsd = null; // ✅ for Perplexity (and any provider that reports cost)

            if (root.TryGetProperty("usage", out var usage))
            {
                // ---- Tokens (support both OpenAI + Perplexity-style fields)
                inputTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32()
                            : usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;

                outputTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32()
                             : usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;

                if (usage.TryGetProperty("prompt_tokens_details", out var ptd) &&
                    ptd.TryGetProperty("cached_tokens", out var cachedProp))
                {
                    cachedInputTokens = Math.Max(0, cachedProp.GetInt32());
                }

                // ✅ Perplexity cost block:
                // usage.cost.total_cost
                if (usage.TryGetProperty("cost", out var costObj) &&
                    costObj.TryGetProperty("total_cost", out var totalCostProp) &&
                    totalCostProp.ValueKind == JsonValueKind.Number)
                {
                    providerReportedCostUsd = totalCostProp.GetDouble();
                }
            }

            var newInputTokens = Math.Max(0, inputTokens - cachedInputTokens);

            // ---- If provider reports cost, prefer it (Perplexity already includes request cost)
            double costUsd;

            if (providerReportedCostUsd.HasValue)
            {
                costUsd = providerReportedCostUsd.Value;
            }
            else
            {
                // ---- OpenAI pricing path (your existing logic)
                string normalizedModel = model switch
                {
                    var m when m.StartsWith("gpt-5-mini", StringComparison.OrdinalIgnoreCase) => "gpt-5-mini",
                    var m when m.StartsWith("gpt-5-nano", StringComparison.OrdinalIgnoreCase) => "gpt-5-nano",
                    var m when m.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) => "gpt-5",
                    var m when m.StartsWith("gpt-4o", StringComparison.OrdinalIgnoreCase) => "gpt-4o",
                    var m when m.StartsWith("gpt-4-turbo", StringComparison.OrdinalIgnoreCase) => "gpt-4-turbo",
                    var m when m.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase) => "gpt-4",
                    var m when m.StartsWith("gpt-3.5-turbo", StringComparison.OrdinalIgnoreCase) => "gpt-3.5-turbo",
                    var m when m.StartsWith("text-embedding-ada-002-v2", StringComparison.OrdinalIgnoreCase) => "text-embedding-ada-002-v2",
                    var m when m.StartsWith("text-embedding-ada-002", StringComparison.OrdinalIgnoreCase) => "text-embedding-ada-002",
                    _ => model
                };

                (double inPer1k, double cachedInPer1k, double outPer1k) = normalizedModel switch
                {
                    "gpt-5" => (0.00125, 0.000125, 0.01000),
                    "gpt-5-mini" => (0.00025, 0.000025, 0.00200),
                    "gpt-5-nano" => (0.00005, 0.000005, 0.00040),

                    "gpt-4o" => (0.00500, 0.00500, 0.01500),
                    "gpt-4" => (0.03000, 0.03000, 0.06000),
                    "gpt-4-turbo" => (0.01000, 0.01000, 0.03000),
                    "gpt-3.5-turbo" => (0.00150, 0.00150, 0.00200),

                    "text-embedding-ada-002" => (0.00010, 0.00010, 0.00000),
                    "text-embedding-ada-002-v2" => (0.00002, 0.00002, 0.00000),

                    _ => (0.0, 0.0, 0.0)
                };

                costUsd =
                    (newInputTokens / 1000.0) * inPer1k +
                    (cachedInputTokens / 1000.0) * cachedInPer1k +
                    (outputTokens / 1000.0) * outPer1k;
            }

            // ---- Persist
            using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.AiUsageLogs.Add(new AiUsageLog
            {
                Model = model,
                PromptTokens = inputTokens,
                CompletionTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                CostUsd = Math.Round(costUsd, 4),
                Purpose = purpose,
                CompanyId = companyId,
                Prompt = NormalizeMultilinePrompt(prompt)
            });

            await dbContext.SaveChangesAsync();
        }

        public async Task LogAsync(JsonElement openAiResponseElement, string purpose, int? companyId = null, string? prompt = null)
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, openAiResponseElement);
            stream.Position = 0;

            using var doc = await JsonDocument.ParseAsync(stream);
            await LogAsync(doc, purpose, companyId, prompt);
        }
        public static string NormalizeMultilinePrompt(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            raw = raw.Replace("        ", "");

            var lines = raw
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd())
                .ToList();

            int commonIndent = lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.TakeWhile(char.IsWhiteSpace).Count())
                .DefaultIfEmpty(0)
                .Min();

            var normalized = lines
                .Select(line => line.Length >= commonIndent ? line.Substring(commonIndent) : line)
                .ToList();

            return string.Join("\r\n", normalized);
        }
    }
}
