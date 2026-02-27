using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure
{
    public class FeedbackParserResolver : IFeedbackParserResolver
    {
        private readonly Dictionary<DataSourceTypeEnum, IFeedbackParser> _map;

        public FeedbackParserResolver(IEnumerable<IFeedbackParser> parsers)
        {
            _map = new();

            foreach (var p in parsers)
            {
                if (!_map.TryAdd(p.SupportedType, p))
                    throw new InvalidOperationException($"Multiple parsers registered for {p.SupportedType}.");
            }
        }

        public IFeedbackParser Get(DataSourceTypeEnum type)
            => _map.TryGetValue(type, out var p)
                ? p
                : throw new InvalidOperationException($"No parser registered for {type}");
    }
}
