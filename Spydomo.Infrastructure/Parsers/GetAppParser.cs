using HtmlAgilityPack;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Globalization;

namespace Spydomo.Infrastructure.Parsers
{
    public class GetAppParser : IFeedbackParser
    {
        public DataSourceTypeEnum SupportedType => DataSourceTypeEnum.GetApp;
        public async Task<List<RawContent>> Parse(string htmlResponse, int companyId, DataSource source, DateTime? lastUpdate, OriginTypeEnum originType = OriginTypeEnum.UserGenerated)
        {
            var reviews = new List<RawContent>();

            throw new NotImplementedException();

            return reviews;
        }

        private string ExtractSection(HtmlNode reviewNode, string dataTestId)
        {
            var sectionNode = reviewNode.SelectSingleNode($".//*[@data-testid='{dataTestId}']");
            return HtmlEntity.DeEntitize(sectionNode?.InnerText.Trim()) ?? "N/A";
        }

        private DateTime? ParseReviewDate(string dateString)
        {
            if (DateTime.TryParseExact(dateString, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate;
            }
            return null;
        }

        public async Task<string> FetchRawContentAsync(string url, DateTime? lastUpdate)
        {
            return null;
        }
    }
}
