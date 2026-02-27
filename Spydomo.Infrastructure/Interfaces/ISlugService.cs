using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISlugService
    {
        Task<string> GenerateUniqueSlugAsync(string input, EntityType type);
    }
}
