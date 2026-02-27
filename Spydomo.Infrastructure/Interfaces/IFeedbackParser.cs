using Spydomo.Common.Enums;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IFeedbackParser
    {
        DataSourceTypeEnum SupportedType { get; }
        Task<List<RawContent>> Parse(
            string jsonResponse,
            int companyId,
            DataSource source,
            DateTime? lastUpdate,
            OriginTypeEnum originType = OriginTypeEnum.UserGenerated);
        Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate);
    }
}
