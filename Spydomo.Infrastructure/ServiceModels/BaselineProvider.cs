using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.PulseRules;

namespace Spydomo.Infrastructure.ServiceModels
{
    public sealed class BaselineProvider : IBaselineProvider
    {
        private readonly Dictionary<(int CompanyId, int Days), int> _reviewsCache = new();
        private readonly Dictionary<(int CompanyId, string Theme, int Days), int> _themePosts = new();
        private readonly Dictionary<(int CompanyId, string Theme, int Days), int> _negThemeCounts = new();
        private readonly Dictionary<(int CompanyId, DataSourceTypeEnum Channel, int Days), double> _channelShare = new();

        public BaselineProvider(IEnumerable<Spydomo.Models.SummarizedInfo> sis, DateTime nowUtc)
        {
            var list = sis.ToList();

            // Reviews per X days
            foreach (var si in list)
            {
                var days = 30; // default baseline window
                if (PulseUtils.IsReviewSource(si.SourceTypeEnum)
                    && si.Date.HasValue)
                {
                    var key = (si.CompanyId, days);
                    _reviewsCache[key] = _reviewsCache.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }

            // Theme posts counts (14d and 90d)
            foreach (var window in new[] { 14, 90 })
            {
                foreach (var si in list)
                {
                    if (!PulseUtils.IsContentSource(si.SourceTypeEnum) || si.Date is null) continue;
                    if (si.Date < nowUtc.AddDays(-window)) continue;

                    var themes = si.SummarizedInfoThemes?.Select(t => t.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct() ?? Enumerable.Empty<string>();
                    foreach (var theme in themes)
                    {
                        var key = (si.CompanyId, theme!, window);
                        _themePosts[key] = _themePosts.TryGetValue(key, out var c) ? c + 1 : 1;
                    }

                    // crude channel share accumulation: count per channel then convert to share later
                    var channel = si.SourceTypeEnum;
                    if (channel is null) continue; // unknown/missing source type

                    var chKey = (si.CompanyId, channel.Value, window);
                    _channelShare[chKey] = _channelShare.TryGetValue(chKey, out var c2) ? c2 + 1 : 1;

                }
            }

            // Convert channel counts to share per company/window
            foreach (var window in new[] { 30 })
            {
                var perCompany = _channelShare.Keys
                    .Where(k => k.Days == 30)
                    .GroupBy(k => (k.CompanyId, k.Days))
                    .ToList();

                foreach (var grp in perCompany)
                {
                    double total = grp.Sum(k => _channelShare[(k.CompanyId, k.Channel, k.Days)]);
                    foreach (var k in grp)
                    {
                        _channelShare[(k.CompanyId, k.Channel, k.Days)] =
                            total <= 0 ? 0 : _channelShare[(k.CompanyId, k.Channel, k.Days)] / total;
                    }
                }
            }

            // Negative theme counts (30d)
            foreach (var si in list)
            {
                if (si.Date is null || si.Date < nowUtc.AddDays(-30)) continue;
                if (si.Sentiment != Spydomo.Common.Enums.SentimentEnum.Negative) continue;

                var themes = si.SummarizedInfoThemes?.Select(t => t.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct() ?? Enumerable.Empty<string>();
                foreach (var theme in themes)
                {
                    var key = (si.CompanyId, theme!, 30);
                    _negThemeCounts[key] = _negThemeCounts.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
        }

        public int ReviewsInLastDays(int companyId, int days) =>
            _reviewsCache.TryGetValue((companyId, days), out var n) ? n : 0;

        public int ThemePosts(int companyId, string theme, int days) =>
            _themePosts.TryGetValue((companyId, theme, days), out var n) ? n : 0;

        public double ChannelShare(int companyId, DataSourceTypeEnum channel, int days) =>
            _channelShare.TryGetValue((companyId, channel, days), out var s) ? s : 0.0;

        public int NegativeThemeCount(int companyId, string theme, int days) =>
            _negThemeCounts.TryGetValue((companyId, theme, days), out var n) ? n : 0;
    }
}
