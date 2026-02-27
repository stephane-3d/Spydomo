using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spydomo.Infrastructure
{
    public sealed class SemanticSignalRepository : ISemanticSignalRepository
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        public SemanticSignalRepository(IDbContextFactory<SpydomoContext> dbFactory) => _dbFactory = dbFactory;

        public async Task<SemanticSignal?> GetByHashAsync(string hash, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.SemanticSignals
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Hash == hash, ct);
        }

        public async Task UpsertAsync(SemanticSignal row, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var existing = await db.SemanticSignals.FirstOrDefaultAsync(x => x.Hash == row.Hash, ct);
            if (existing is null)
            {
                db.SemanticSignals.Add(row);
            }
            else
            {
                existing.CompanyId = row.CompanyId;
                existing.SourceType = row.SourceType;
                existing.RawContentId = row.RawContentId;
                existing.SummarizedInfoId = row.SummarizedInfoId;
                existing.SeenAt = row.SeenAt;
                existing.Lang = row.Lang;
                existing.Classifier = row.Classifier;
                existing.IntentsJson = row.IntentsJson;
                existing.KeywordsJson = row.KeywordsJson;
                existing.ModelScore = row.ModelScore;
                // keep existing.Embedding; only set in UpdateEmbeddingAsync
            }
            await db.SaveChangesAsync(ct);
        }

        public async Task UpdateEmbeddingAsync(string hash, byte[] embedding, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var row = await db.SemanticSignals.FirstOrDefaultAsync(x => x.Hash == hash, ct);
            if (row is null) return;
            row.Embedding = embedding;
            await db.SaveChangesAsync(ct);
        }

        public async Task<List<SemanticSignal>> QueryForEmbeddingBackfillAsync(int take = 200, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.SemanticSignals
                .AsNoTracking()
                .Where(x => x.Embedding == null)
                .OrderByDescending(x => x.CreatedAt)
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task<int> CountIntentSinceAsync(int companyId, Intent intent, DateTime since, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Coarse prefilter: company + time (avoid full table scan)
            var candidates = await db.SemanticSignals.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.SeenAt >= since)
                .Select(x => x.IntentsJson)
                .ToListAsync(ct);

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() } // enums as strings in JSON
            };

            var total = 0;
            foreach (var json in candidates)
            {
                try
                {
                    var intents = JsonSerializer.Deserialize<List<IntentHit>>(json ?? "[]", opts) ?? new();
                    if (intents.Any(i => i.Name == intent))
                        total++;
                }
                catch { /* ignore malformed rows */ }
            }
            return total;
        }
    }
}
