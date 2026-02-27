using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IBaselineProvider
    {
        int ReviewsInLastDays(int companyId, int days);
        int ThemePosts(int companyId, string theme, int days);
        double ChannelShare(int companyId, DataSourceTypeEnum channel, int days);
        int NegativeThemeCount(int companyId, string theme, int days);
    }

}
