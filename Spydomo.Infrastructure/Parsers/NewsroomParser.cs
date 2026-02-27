using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Parsers
{
    public class NewsroomParser : IFeedbackParser
    {
        private readonly CompanyContentParser _inner;
        public NewsroomParser(CompanyContentParser inner) => _inner = inner;
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.News;

        public Task<List<RawContent>> Parse(string html, int companyId, DataSource dataSource, DateTime? lastUpdate, OriginTypeEnum originType = OriginTypeEnum.CompanyGenerated)
            => _inner.Parse(html, companyId, dataSource, lastUpdate);

        public Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
            => _inner.FetchRawContentAsync(url, lastUpdate);
    }

}
