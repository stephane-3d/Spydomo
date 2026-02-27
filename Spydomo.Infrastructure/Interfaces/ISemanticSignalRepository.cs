using Spydomo.Common.Enums;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISemanticSignalRepository
    {
        Task<SemanticSignal?> GetByHashAsync(string hash, CancellationToken ct = default);
        Task UpsertAsync(SemanticSignal row, CancellationToken ct = default);
        Task UpdateEmbeddingAsync(string hash, byte[] embedding, CancellationToken ct = default);
        Task<List<SemanticSignal>> QueryForEmbeddingBackfillAsync(int take = 200, CancellationToken ct = default);
        Task<int> CountIntentSinceAsync(int companyId, Intent intent, DateTime since, CancellationToken ct = default);
    }
}
