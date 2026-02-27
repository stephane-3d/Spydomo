using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IPostingWindowStatsRepository
    {
        Task<PostingWindowStats?> GetAsync(int companyId, string periodType, CancellationToken ct = default);
    }
}
