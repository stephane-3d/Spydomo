using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISemanticClassifier
    {
        Task<IntentResult> ClassifyAsync(TextSample sample, CancellationToken ct = default);
    }
}
