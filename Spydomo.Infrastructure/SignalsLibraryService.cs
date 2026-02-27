using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Spydomo.DTO.SignalLibrary;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public sealed class SignalsLibraryService : ISignalsLibraryService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly IMemoryCache _cache;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        public SignalsLibraryService(
            IDbContextFactory<SpydomoContext> dbFactory,
            IMemoryCache cache)
        {
            _dbFactory = dbFactory;
            _cache = cache;
        }

        private static (DateTime from30, DateTime from60, DateTime now) Windows(DateTime utcNow)
        {
            var now = utcNow;
            var from30 = now.AddDays(-30);
            var from60 = now.AddDays(-60);
            return (from30, from60, now);
        }

        private static decimal DeltaPct(int last, int prev)
        {
            if (prev <= 0) return last > 0 ? 1m : 0m;
            return (decimal)(last - prev) / prev;
        }

        private static DateTime FloorTo30MinutesUtc(DateTime utcNow)
        {
            utcNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
            var minutes = utcNow.Minute - (utcNow.Minute % 30);
            return new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, minutes, 0, DateTimeKind.Utc);
        }

        private static MemoryCacheEntryOptions CacheOptions() =>
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration,
                SlidingExpiration = TimeSpan.FromMinutes(10)
            };

        public async Task<List<SignalsIndexRow>> GetCategoriesIndexAsync(DateTime utcNow, CancellationToken ct)
        {
            var bucket = FloorTo30MinutesUtc(utcNow);
            var cacheKey = $"signals:index:{bucket:yyyyMMddHHmm}";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.SetOptions(CacheOptions());

                var (from30, from60, now) = Windows(bucket);

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var rows = await (
                    from cat in db.CompanyCategories.AsNoTracking()
                    join c in db.Companies.AsNoTracking() on cat.Id equals c.PrimaryCategoryId into catCompanies
                    from c in catCompanies.DefaultIfEmpty()
                    join si in db.SummarizedInfos.AsNoTracking() on c.Id equals si.CompanyId into sis
                    from si in sis.DefaultIfEmpty()
                    join st in db.SummarizedInfoSignalTypes.AsNoTracking() on si.Id equals st.SummarizedInfoId into sts
                    from st in sts.DefaultIfEmpty()
                    where si.Date >= from60 && si.Date < now
                    select new
                    {
                        cat.Slug,
                        cat.Name,
                        cat.Description,
                        PostedDate = si.Date,
                        HasSignal = st != null
                    }
                )
                .Where(x => x.HasSignal)
                .GroupBy(x => new { x.Slug, x.Name, x.Description })
                .Select(g => new
                {
                    g.Key.Slug,
                    g.Key.Name,
                    g.Key.Description,
                    Last30 = g.Count(x => x.PostedDate >= from30 && x.PostedDate < now),
                    Prev30 = g.Count(x => x.PostedDate >= from60 && x.PostedDate < from30),
                })
                .OrderByDescending(x => x.Last30)
                .ToListAsync(ct);

                return rows.Select(x => new SignalsIndexRow
                {
                    CategorySlug = x.Slug,
                    CategoryName = x.Name,
                    CategoryDescription = x.Description,
                    Last30Count = x.Last30,
                    Prev30Count = x.Prev30,
                    DeltaPct = DeltaPct(x.Last30, x.Prev30)
                }).ToList();
            }) ?? new List<SignalsIndexRow>();
        }

        public async Task<(string CategoryName, List<SignalTypeRow> SignalTypes)> GetCategoryAsync(
            string categorySlug,
            DateTime utcNow,
            CancellationToken ct)
        {
            var bucket = FloorTo30MinutesUtc(utcNow);
            var cacheKey = $"signals:category:{categorySlug}:{bucket:yyyyMMddHHmm}";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.SetOptions(CacheOptions());

                var (from30, from60, now) = Windows(bucket);

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var cat = await db.CompanyCategories.AsNoTracking()
                    .Where(x => x.Slug == categorySlug)
                    .Select(x => new { x.Id, x.Name })
                    .FirstOrDefaultAsync(ct);

                if (cat == null)
                    return ("", new List<SignalTypeRow>());

                var rows = await (
                    from c in db.Companies.AsNoTracking()
                    where c.PrimaryCategoryId == cat.Id
                    join si in db.SummarizedInfos.AsNoTracking() on c.Id equals si.CompanyId
                    join sit in db.SummarizedInfoSignalTypes.AsNoTracking() on si.Id equals sit.SummarizedInfoId
                    join st in db.SignalTypes.AsNoTracking() on sit.SignalTypeId equals st.Id
                    where si.Date >= from60 && si.Date < now
                       && st.AllowedInLlm
                    select new
                    {
                        st.Slug,
                        st.Name,
                        st.Description,
                        si.Date
                    }
                )
                .GroupBy(x => new { x.Slug, x.Name, x.Description })
                .Select(g => new
                {
                    g.Key.Slug,
                    g.Key.Name,
                    g.Key.Description,
                    Last30 = g.Count(x => x.Date >= from30 && x.Date < now),
                    Prev30 = g.Count(x => x.Date >= from60 && x.Date < from30),
                })
                .OrderByDescending(x => x.Last30)
                .ToListAsync(ct);

                var signalTypes = rows.Select(x => new SignalTypeRow
                {
                    SignalTypeSlug = x.Slug,
                    SignalTypeName = x.Name,
                    Description = x.Description,
                    Last30Count = x.Last30,
                    Prev30Count = x.Prev30,
                    DeltaPct = DeltaPct(x.Last30, x.Prev30),
                }).ToList();

                return (cat.Name, signalTypes);
            });
        }

        public async Task<(string CategoryName, string SignalTypeName, string SignalTypeDescription, List<ThemeRow> Themes)> GetCategorySignalAsync(
            string categorySlug,
            string signalTypeSlug,
            DateTime utcNow,
            CancellationToken ct)
        {
            var bucket = FloorTo30MinutesUtc(utcNow);
            var cacheKey = $"signals:category-signal:{categorySlug}:{signalTypeSlug}:{bucket:yyyyMMddHHmm}";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.SetOptions(CacheOptions());

                var (from30, from60, now) = Windows(bucket);

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var cat = await db.CompanyCategories.AsNoTracking()
                    .Where(x => x.Slug == categorySlug)
                    .Select(x => new { x.Id, x.Name })
                    .FirstOrDefaultAsync(ct);

                if (cat == null)
                    return ("", "", "", new List<ThemeRow>());

                var st = await db.SignalTypes.AsNoTracking()
                    .Where(x => x.Slug == signalTypeSlug)
                    .Select(x => new { x.Id, x.Name, x.Description })
                    .FirstOrDefaultAsync(ct);

                if (st == null)
                    return (cat.Name, "", "", new List<ThemeRow>());

                var rows = await (
                    from c in db.Companies.AsNoTracking()
                    where c.PrimaryCategoryId == cat.Id
                    join si in db.SummarizedInfos.AsNoTracking() on c.Id equals si.CompanyId
                    join sit in db.SummarizedInfoSignalTypes.AsNoTracking() on si.Id equals sit.SummarizedInfoId
                    join sith in db.SummarizedInfoThemes.AsNoTracking() on si.Id equals sith.SummarizedInfoId
                    join th in db.CanonicalThemes.AsNoTracking() on sith.CanonicalThemeId equals th.Id
                    where si.Date >= from60 && si.Date < now
                       && sit.SignalTypeId == st.Id
                    select new
                    {
                        th.Slug,
                        th.Name,
                        th.Description,
                        si.Date
                    }
                )
                .GroupBy(x => new { x.Slug, x.Name, x.Description })
                .Select(g => new
                {
                    g.Key.Slug,
                    g.Key.Name,
                    g.Key.Description,
                    Last30 = g.Count(x => x.Date >= from30 && x.Date < now),
                    Prev30 = g.Count(x => x.Date >= from60 && x.Date < from30),
                })
                .OrderByDescending(x => x.Last30)
                .ToListAsync(ct);

                var themes = rows.Select(x => new ThemeRow
                {
                    ThemeSlug = x.Slug,
                    ThemeName = x.Name,
                    Description = x.Description,
                    Last30Count = x.Last30,
                    Prev30Count = x.Prev30,
                    DeltaPct = DeltaPct(x.Last30, x.Prev30),
                }).ToList();

                return (cat.Name, st.Name, st.Description, themes);
            });
        }

        public async Task<ThemeDetailVm?> GetThemeDetailAsync(
            string categorySlug,
            string signalTypeSlug,
            string themeSlug,
            DateTime utcNow,
            CancellationToken ct)
        {
            var bucket = FloorTo30MinutesUtc(utcNow);
            var cacheKey = $"signals:theme-detail:{categorySlug}:{signalTypeSlug}:{themeSlug}:{bucket:yyyyMMddHHmm}";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.SetOptions(CacheOptions());

                var (from30, from60, now) = Windows(bucket);

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var cat = await db.CompanyCategories.AsNoTracking()
                    .Where(x => x.Slug == categorySlug)
                    .Select(x => new { x.Id, x.Name })
                    .FirstOrDefaultAsync(ct);
                if (cat == null) return null;

                var st = await db.SignalTypes.AsNoTracking()
                    .Where(x => x.Slug == signalTypeSlug)
                    .Select(x => new { x.Id, x.Name })
                    .FirstOrDefaultAsync(ct);
                if (st == null) return null;

                var th = await db.CanonicalThemes.AsNoTracking()
                    .Where(x => x.Slug == themeSlug)
                    .Select(x => new { x.Id, x.Name, x.Description })
                    .FirstOrDefaultAsync(ct);
                if (th == null) return null;

                var counts = await (
                    from c in db.Companies.AsNoTracking()
                    where c.PrimaryCategoryId == cat.Id
                    join si in db.SummarizedInfos.AsNoTracking() on c.Id equals si.CompanyId
                    join sit in db.SummarizedInfoSignalTypes.AsNoTracking() on si.Id equals sit.SummarizedInfoId
                    join sith in db.SummarizedInfoThemes.AsNoTracking() on si.Id equals sith.SummarizedInfoId
                    where si.Date >= from60 && si.Date < now
                       && sit.SignalTypeId == st.Id
                       && sith.CanonicalThemeId == th.Id
                    select new { si.Date }
                )
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Last30 = g.Count(x => x.Date >= from30 && x.Date < now),
                    Prev30 = g.Count(x => x.Date >= from60 && x.Date < from30),
                })
                .FirstOrDefaultAsync(ct);

                var last30 = counts?.Last30 ?? 0;
                var prev30 = counts?.Prev30 ?? 0;

                var examples = await (
                    from c in db.Companies.AsNoTracking()
                    where c.PrimaryCategoryId == cat.Id
                    join si in db.SummarizedInfos.AsNoTracking() on c.Id equals si.CompanyId
                    join sit in db.SummarizedInfoSignalTypes.AsNoTracking() on si.Id equals sit.SummarizedInfoId
                    join sith in db.SummarizedInfoThemes.AsNoTracking() on si.Id equals sith.SummarizedInfoId
                    join rc in db.RawContents.AsNoTracking() on si.RawContentId equals rc.Id into rcj
                    from rc in rcj.DefaultIfEmpty()
                    where si.Date >= from30 && si.Date < now
                       && sit.SignalTypeId == st.Id
                       && sith.CanonicalThemeId == th.Id
                    orderby si.Date descending
                    select new ThemeExampleRow
                    {
                        PostedDate = si.Date,
                        CompanyName = c.Name ?? "",
                        Gist = si.Gist,
                        SignalReason = sit.Reason,
                        SummarizedInfoId = si.Id,
                        Url = rc != null ? rc.PostUrl : null
                    }
                )
                .Take(30)
                .ToListAsync(ct);

                return new ThemeDetailVm
                {
                    CategorySlug = categorySlug,
                    CategoryName = cat.Name,
                    SignalTypeSlug = signalTypeSlug,
                    SignalTypeName = st.Name,
                    ThemeSlug = themeSlug,
                    ThemeName = th.Name,
                    ThemeDescription = th.Description,
                    Last30Count = last30,
                    DeltaPct = DeltaPct(last30, prev30),
                    Examples = examples
                };
            });
        }
    }
}