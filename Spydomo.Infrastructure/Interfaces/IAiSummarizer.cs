using Spydomo.Common.Enums;
using Spydomo.Infrastructure.ServiceModels;


namespace Spydomo.Infrastructure.Interfaces
{
    public interface IAiSummarizer
    {
        Task<Dictionary<int, AiSummaryResult>> SummarizeBatchAsync(List<(int Id, string CanonicalText, OriginTypeEnum OriginType)> batch,
            int? companyId = null,
            CancellationToken ct = default);
    }
}
