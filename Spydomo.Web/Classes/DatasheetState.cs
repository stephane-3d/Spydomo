using Spydomo.Common.Enums;
using Spydomo.DTO.Datasheet;

namespace Spydomo.Web.Classes
{
    public readonly record struct OverviewKey(int Period, int? GroupId, int? CompanyId, string SearchNorm)
    {
        public static OverviewKey From(Period p, int? g, int? c, string? s)
            => new((int)p, g, c, (s ?? "").Trim().ToLowerInvariant());
    }

    public readonly record struct SourcesKey(int Period, int? GroupId, int? CompanyId, string SearchNorm)
    {
        public static SourcesKey From(Period p, int? g, int? c, string? s)
            => new((int)p, g, c, (s ?? "").Trim().ToLowerInvariant());
    }

    public readonly record struct KeywordsKey(int Period, int? GroupId, int? CompanyId, string Q)
    {
        public static KeywordsKey From(Period p, int? groupId, int? companyId, string? q)
            => new((int)p, groupId, companyId, q ?? string.Empty);
    }

    public readonly record struct ThemesKey(int Period, int? GroupId, int? CompanyId, string Q, string Orig)
    {
        public static ThemesKey From(Period p, int? groupId, int? companyId, string? q, string? orig)
            => new((int)p, groupId, companyId, q ?? string.Empty, (orig ?? "all").ToLowerInvariant());
    }

    public readonly record struct TagsKey(int Period, int? GroupId, int? CompanyId, string Q, string Sent, string Orig)
    {
        public static TagsKey From(Period p, int? groupId, int? companyId, string? q, string? sent, string? orig)
            => new((int)p, groupId, companyId, q ?? string.Empty, (sent ?? "all").ToLowerInvariant(), (orig ?? "all").ToLowerInvariant());
    }

    public sealed class DatasheetState
    {
        public Period Period { get; set; } = Period.D90;
        public int? GroupId { get; set; }
        public int? CompanyId { get; set; }
        public string Search { get; set; } = "";

        // simple “version” to notify tabs to reload
        public event Action? Changed;
        public void Set(Period p, int? g, int? c, string s) { Period = p; GroupId = g; CompanyId = c; Search = s; Changed?.Invoke(); }

        private readonly Dictionary<OverviewKey, (DateTimeOffset ts, List<OverviewRow> rows)> _overview = new();
        public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);

        private readonly Dictionary<SourcesKey, (DateTimeOffset ts, List<SourcesCompanyDto> rows)> _sources = new();
        public TimeSpan SourcesCacheFor { get; set; } = TimeSpan.FromMinutes(5);

        private readonly Dictionary<KeywordsKey, (List<KeywordsCompanyDto> Rows, DateTime Stamp)> _kwCompanies = new();
        public readonly TimeSpan KeywordsCacheFor = TimeSpan.FromMinutes(5);

        private readonly Dictionary<ThemesKey, (List<ThemesCompanyDto> Rows, DateTime Stamp)> _themes = new();
        public readonly TimeSpan ThemesCacheFor = TimeSpan.FromMinutes(5);

        private readonly Dictionary<TagsKey, (List<TagsCompanyDto> Rows, DateTime Stamp)> _tags = new();
        public readonly TimeSpan TagsCacheFor = TimeSpan.FromMinutes(5);

        public bool TryGetOverview(OverviewKey k, out List<OverviewRow> rows, out TimeSpan age)
        {
            if (_overview.TryGetValue(k, out var entry))
            {
                age = DateTimeOffset.UtcNow - entry.ts;
                rows = entry.rows;
                return true;
            }
            rows = new(); age = default; return false;
        }

        public void PutOverview(OverviewKey k, List<OverviewRow> rows)
            => _overview[k] = (DateTimeOffset.UtcNow, rows);

        // optional: invalidate helpers you can call after edits
        public void InvalidateOverviewForCompany(int companyId)
        {
            var keys = _overview.Keys.ToList();
            foreach (var key in keys)
                if (_overview[key].rows.Any(r => r.Id == companyId))
                    _overview.Remove(key);
        }

        public bool TryGetSources(SourcesKey k, out List<SourcesCompanyDto> rows, out TimeSpan age)
        {
            if (_sources.TryGetValue(k, out var entry))
            {
                age = DateTimeOffset.UtcNow - entry.ts;
                rows = entry.rows;
                return true;
            }
            rows = new(); age = default; return false;
        }
        public void PutSources(SourcesKey k, List<SourcesCompanyDto> rows)
            => _sources[k] = (DateTimeOffset.UtcNow, rows);

        public bool TryGetKeywordsCompanies(KeywordsKey key, out List<KeywordsCompanyDto> rows, out TimeSpan age)
        {
            if (_kwCompanies.TryGetValue(key, out var entry))
            {
                rows = entry.Rows;
                age = DateTime.UtcNow - entry.Stamp;
                return true;
            }
            rows = new(); age = TimeSpan.MaxValue; return false;
        }

        public void PutKeywordsCompanies(KeywordsKey key, List<KeywordsCompanyDto> rows)
            => _kwCompanies[key] = (rows, DateTime.UtcNow);

        public bool TryGetThemesCompanies(ThemesKey key, out List<ThemesCompanyDto> rows, out TimeSpan age)
        {
            if (_themes.TryGetValue(key, out var entry))
            {
                rows = entry.Rows;
                age = DateTime.UtcNow - entry.Stamp;
                return true;
            }
            rows = new(); age = TimeSpan.MaxValue; return false;
        }

        public void PutThemesCompanies(ThemesKey key, List<ThemesCompanyDto> rows)
            => _themes[key] = (rows, DateTime.UtcNow);

        public bool TryGetTagsCompanies(TagsKey key, out List<TagsCompanyDto> rows, out TimeSpan age)
        {
            if (_tags.TryGetValue(key, out var entry))
            {
                rows = entry.Rows;
                age = DateTime.UtcNow - entry.Stamp;
                return true;
            }
            rows = new(); age = TimeSpan.MaxValue; return false;
        }

        public void PutTagsCompanies(TagsKey key, List<TagsCompanyDto> rows)
            => _tags[key] = (rows, DateTime.UtcNow);
    }
}
