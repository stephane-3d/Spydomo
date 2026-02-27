using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure
{
    public class FeedbackParserFactory
    {
        private readonly Dictionary<DataSourceTypeEnum, IFeedbackParser> _parserMap;

        public FeedbackParserFactory(IEnumerable<IFeedbackParser> parsers)
        {
            _parserMap = parsers
            .GroupBy(p => p.SupportedType)
            .ToDictionary(
                g => g.Key,
                g => g.Count() == 1
                    ? g.First()
                    : throw new InvalidOperationException($"Multiple parsers registered for {g.Key}.")
            );
        }

        public IFeedbackParser? GetParser(DataSourceTypeEnum platform)
        {
            return _parserMap.TryGetValue(platform, out var parser) ? parser : null;
        }
    }


}
