using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IRelevanceEvaluator
    {
        Task<bool> IsContentRelevantAsync(int companyId, string content, DataSourceTypeEnum sourceType);
    }

}
