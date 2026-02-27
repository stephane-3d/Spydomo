using Spydomo.Infrastructure.ServiceModels;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ITagNormalizer
    {
        Task<TagNormalizerResult> NormalizeAsync(string rawTag, string reason, int? companyId = null, CancellationToken ct = default);
    }

}
