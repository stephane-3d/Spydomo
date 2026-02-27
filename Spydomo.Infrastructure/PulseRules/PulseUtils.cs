using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.PulseRules
{
    public static class PulseUtils
    {
        public static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

        public static double ZScore(double value, double mean, double stdev) =>
            stdev <= 0.00001 ? 0.0 : (value - mean) / stdev;

        public static string? TopTheme(Spydomo.Models.SummarizedInfo si) =>
            si.SummarizedInfoThemes?.OrderByDescending(t => t.Label?.Length ?? 0)
              .FirstOrDefault()?.Label;

        public static bool IsReviewSource(DataSourceTypeEnum? t) =>
            t is DataSourceTypeEnum.G2
              or DataSourceTypeEnum.Capterra
              or DataSourceTypeEnum.TrustRadius
              or DataSourceTypeEnum.GetApp
              or DataSourceTypeEnum.SoftwareAdvice
              or DataSourceTypeEnum.GartnerPeerInsights
              or DataSourceTypeEnum.FacebookReviews;

        public static bool IsCommunitySource(DataSourceTypeEnum? t) =>
            t is DataSourceTypeEnum.Reddit
              or DataSourceTypeEnum.Linkedin;

        public static bool IsContentSource(DataSourceTypeEnum? t) =>
            t is DataSourceTypeEnum.Blog
              or DataSourceTypeEnum.Linkedin
              or DataSourceTypeEnum.Facebook
              or DataSourceTypeEnum.Instagram
              or DataSourceTypeEnum.News
              or DataSourceTypeEnum.EmailNewsletters
              or DataSourceTypeEnum.CompanyContent;
    }
}
