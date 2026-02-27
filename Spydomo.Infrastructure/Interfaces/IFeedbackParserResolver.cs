using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IFeedbackParserResolver
    {
        IFeedbackParser Get(DataSourceTypeEnum type);
    }
}
