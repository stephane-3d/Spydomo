using Spydomo.Infrastructure.ServiceModels;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IThemeNormalizer
    {
        Task<ThemeNormalizerResult> NormalizeAsync(string rawTheme, string reason, int? companyId = null, CancellationToken ct = default);
    }
}
