using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.DTO.Datasheet;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public sealed class DatasheetService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ICurrentUserState _current;
        private readonly ILogger<DatasheetService> _log;

        public DatasheetService(
            IDbContextFactory<SpydomoContext> dbFactory,
            ICurrentUserState current,
            ILogger<DatasheetService> log)
        {
            _dbFactory = dbFactory;
            _current = current;
            _log = log;
        }

        public async Task<List<OverviewRow>> GetOverviewAsync(Period p, int? groupId, int? companyId, string? q, CancellationToken ct)
        {
            var clientId = await _current.GetClientIdAsync(ct);
            if (clientId is null) return new();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            IQueryable<TrackedCompany> tcs = db.TrackedCompanies
                .AsNoTracking()
                .Where(tc => tc.ClientId == clientId);

            if (companyId is not null)
                tcs = tcs.Where(tc => tc.CompanyId == companyId.Value);

            if (groupId is not null)
                tcs = tcs.Where(tc => tc.TrackedCompanyGroups.Any(g => g.CompanyGroupId == groupId.Value));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                tcs = tcs.Where(tc =>
                    (tc.Company.Name ?? "").ToLower().Contains(term) ||
                    (tc.Company.Url ?? "").ToLower().Contains(term) ||
                    (tc.Company.SelfPositioning ?? "").ToLower().Contains(term) ||
                    (tc.Company.SelfTitle ?? "").ToLower().Contains(term) ||
                    (tc.Company.SelfDescription ?? "").ToLower().Contains(term));
            }

            var list = await tcs
                .Select(tc => new
                {
                    tc.Company.Id,
                    Name = tc.Company.Name,
                    Url = tc.Company.Url,

                    Category = tc.Company.PrimaryCategory != null ? tc.Company.PrimaryCategory.Name : "",
                    CategorySlug = tc.Company.PrimaryCategory != null ? tc.Company.PrimaryCategory.Slug : null,

                    tc.Company.CategoryReason,
                    tc.Company.CategoryConfidence,
                    tc.Company.CategoryEvidenceJson,

                    tc.Company.SelfPositioning,
                    tc.Company.SelfTitle,
                    tc.Company.SelfDescription,

                    GroupName = groupId != null
                        ? tc.TrackedCompanyGroups
                              .Where(x => x.CompanyGroupId == groupId)
                              .Select(x => x.CompanyGroup.Name)
                              .FirstOrDefault()
                        : tc.TrackedCompanyGroups
                              .Select(x => x.CompanyGroup.Name)
                              .OrderBy(n => n)
                              .FirstOrDefault(),

                    Personas = tc.Company.CompanyUserPersonas
                        .OrderBy(x => x.UserPersona.Name)
                        .Select(x => x.UserPersona.Name)
                        .ToList(),

                    Segments = tc.Company.CompanyTargetSegments
                        .OrderBy(x => x.TargetSegment.Name)
                        .Select(x => x.TargetSegment.Name)
                        .ToList()
                })
                .ToListAsync(ct);

            var result = new List<OverviewRow>(list.Count);
            foreach (var x in list)
            {
                result.Add(new OverviewRow
                {
                    Id = x.Id,
                    Group = x.GroupName ?? "-",
                    Name = x.Name ?? "",
                    Url = x.Url ?? "",
                    Category = x.Category ?? "",
                    CategorySlug = x.CategorySlug,
                    CategoryReason = x.CategoryReason,
                    CategoryConfidence = x.CategoryConfidence ?? 0m,
                    CategoryEvidence = ParseEvidence(x.CategoryEvidenceJson),
                    SelfPositioning = x.SelfPositioning,
                    SelfTitle = x.SelfTitle,
                    SelfDescription = x.SelfDescription,
                    Personas = x.Personas ?? new(),
                    Segments = x.Segments ?? new()
                });
            }

            return result;
        }

        public async Task<List<SourcesCompanyDto>> GetSourcesAsync(
            Period p,
            int? groupId,
            int? companyId,
            string? q,
            CancellationToken ct = default)
        {
            var periodDays = (int)p;
            var clientId = await _current.GetClientIdAsync(ct);
            if (clientId is null) return new List<SourcesCompanyDto>();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // --- 1) Tracked companies for this client (with optional filters) ---
            var tc = db.TrackedCompanies.AsNoTracking().Where(t => t.ClientId == clientId);

            if (groupId is not null)
                tc = tc.Where(t => t.TrackedCompanyGroups.Any(g => g.CompanyGroupId == groupId.Value));

            if (companyId is not null)
                tc = tc.Where(t => t.CompanyId == companyId.Value);

            /*if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLowerInvariant();
                tc = tc.Where(t => (t.Company.Name ?? "").ToLower().Contains(term) ||
                                   (t.Company.Url ?? "").ToLower().Contains(term));
            }*/

            var tracked = await tc
                .Select(t => new { t.CompanyId, Company = t.Company.Name, Url = t.Company.Url })
                .Distinct()
                .ToListAsync(ct);

            if (tracked.Count == 0) return new List<SourcesCompanyDto>();

            var companyIds = tracked.Select(x => x.CompanyId).ToList();

            // --- 2) Link map from DataSources (CompanyId + DataSourceTypeId -> Url) ---
            // If your DataSource entity uses different property names, adjust below.
            // 1) Pull a slim projection
            var dsPairs = await db.DataSources
                .AsNoTracking()
                .Where(ds => ds.CompanyId.HasValue && companyIds.Contains(ds.CompanyId.Value))
                .Where(ds => ds.TypeId != null)
                .Select(ds => new
                {
                    CompanyId = ds.CompanyId!.Value,
                    TypeId = ds.TypeId!.Value,
                    Url = ds.Url ?? ""
                })
                .ToListAsync(ct);

            // 2) Dedupe per (CompanyId, TypeId) and choose a single URL
            var linkMap = dsPairs
                .GroupBy(x => x.CompanyId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => x.TypeId)
                          .ToDictionary(
                              gg => gg.Key,
                              // pick a non-empty URL if any; otherwise empty string
                              gg => gg.Select(x => x.Url)
                                      .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                                    ?? ""
                          )
                );

            // --- 3) Pull events within period from SummarizedInfo and RawContent ---
            var cutoff = DateTime.UtcNow.AddDays(-periodDays);

            // SummarizedInfo (uses SourceTypeId + OriginType)
            var si = await db.SummarizedInfos
                .AsNoTracking()
                .Where(s => companyIds.Contains(s.CompanyId)
                         && ((s.Date ?? s.GistGeneratedAt ?? DateTime.MinValue) >= cutoff))
                .Select(s => new
                {
                    s.CompanyId,
                    TypeId = s.SourceTypeId ?? (s.SourceType != null ? (int?)s.SourceType.Id : null),
                    s.OriginType
                })
                .ToListAsync(ct);

            // RawContent (uses DataSourceTypeId + OriginType)
            var rc = await db.RawContents
                .AsNoTracking()
                .Where(r => companyIds.Contains(r.CompanyId) &&
                            (r.PostedDate ?? r.CreatedAt) >= cutoff)
                .Select(r => new
                {
                    CompanyId = r.CompanyId,
                    TypeId = (int?)r.DataSourceTypeId,
                    r.OriginType
                })
                .ToListAsync(ct);

            // Union (ignore rows with null TypeId)
            var all = si.Concat(rc)
                        .Where(x => x.TypeId.HasValue)
                        .ToList();

            // --- 4) Build response per company ---
            var byCompany = all.GroupBy(x => x.CompanyId);

            var result = new List<SourcesCompanyDto>(tracked.Count);
            foreach (var t in tracked.OrderBy(x => x.Company))
            {
                var items = byCompany.FirstOrDefault(g => g.Key == t.CompanyId);

                var compCounts = new Dictionary<int, int>(); // DataSourceTypeId -> count
                var userCounts = new Dictionary<int, int>();

                if (items is not null)
                {
                    foreach (var e in items)
                    {
                        var id = e.TypeId!.Value;
                        if (e.OriginType == OriginTypeEnum.CompanyGenerated)
                            compCounts[id] = (compCounts.TryGetValue(id, out var c1) ? c1 : 0) + 1;
                        else
                            userCounts[id] = (userCounts.TryGetValue(id, out var c2) ? c2 : 0) + 1;
                    }
                }

                // Map to DTO rows with display name + link url
                List<SourceCountDto> Map(Dictionary<int, int> dict)
                    => dict.OrderByDescending(kv => kv.Value)
                           .Select(kv =>
                           {
                               var typeId = kv.Key;
                               var enumVal = (DataSourceTypeEnum)typeId;
                               return new SourceCountDto
                               {
                                   TypeId = typeId,
                                   Name = SourceDisplay(enumVal),
                                   Count = kv.Value,
                                   LinkUrl = linkMap.TryGetValue(t.CompanyId, out var perCompany) &&
                                             perCompany.TryGetValue(typeId, out var url) &&
                                             !string.IsNullOrWhiteSpace(url)
                                             ? url
                                             : null
                               };
                           })
                           .ToList();

                var compDto = Map(compCounts);
                var userDto = Map(userCounts);

                result.Add(new SourcesCompanyDto
                {
                    CompanyId = t.CompanyId,
                    Company = t.Company ?? "(Unknown)",
                    Url = t.Url ?? "",
                    CompanyGenerated = compDto,
                    UserGenerated = userDto
                });
            }

            return result;
        }

        public async Task<List<KeywordsCompanyDto>> GetKeywordsAsync(
            Period p,
            int? groupId,
            int? companyId,
            string? q,
            int take = 10,
            CancellationToken ct = default)
        {
            var periodDays = (int)p;

            var clientId = await _current.GetClientIdAsync(ct);
            if (clientId is null) return new List<KeywordsCompanyDto>();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // 1) Tracked companies for this client (with optional filters)
            var tc = db.TrackedCompanies.AsNoTracking().Where(t => t.ClientId == clientId);

            if (groupId is not null)
                tc = tc.Where(t => t.TrackedCompanyGroups.Any(g => g.CompanyGroupId == groupId.Value));

            if (companyId is not null)
                tc = tc.Where(t => t.CompanyId == companyId.Value);

            var tracked = await tc
                .Select(t => new { t.CompanyId, Company = t.Company.Name, Url = t.Company.Url })
                .Distinct()
                .ToListAsync(ct);

            if (tracked.Count == 0)
                return new List<KeywordsCompanyDto>();

            var companyIds = tracked.Select(x => x.CompanyId).ToList();
            var cutoff = DateTime.UtcNow.AddDays(-periodDays);

            // 2) Pull keywords in period for the tracked companies
            var kwQuery = db.CompanyKeywords
                .AsNoTracking()
                .Where(k => companyIds.Contains(k.CompanyId));

            // Optional: if q was intended to match keyword/reason too, include it here:
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                kwQuery = kwQuery.Where(k =>
                    k.Keyword.Contains(term) ||
                    (k.Reason != null && k.Reason.Contains(term)));
            }

            var rows = await kwQuery
                .Select(k => new KeywordRow
                {
                    Id = k.Id,
                    CompanyId = k.CompanyId,
                    Keyword = k.Keyword,
                    Confidence = k.Confidence,
                    Reason = k.Reason,
                    CreatedAt = k.CreatedAt
                })
                .ToListAsync(ct);

            // 3) Group per company and limit to `take`
            var byCompany = rows
                .GroupBy(r => r.CompanyId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.Confidence)
                          .ThenBy(x => x.Keyword)
                          .Take(Math.Max(1, take))
                          .ToList()
                );

            // 4) Build result
            var result = tracked
                .OrderBy(x => x.Company)
                .Select(t => new KeywordsCompanyDto
                {
                    CompanyId = t.CompanyId,
                    Company = t.Company ?? "(Unknown)",
                    Url = t.Url ?? "",
                    Keywords = byCompany.TryGetValue(t.CompanyId, out var list) ? list : new List<KeywordRow>()
                })
                .ToList();

            return result;
        }

        public async Task<List<ThemesCompanyDto>> GetThemesAsync(
            Period p,
            int? groupId,
            int? companyId,
            string? q,
            string orig = "all",
            int take = 10,
            CancellationToken ct = default)

        {
            var periodDays = (int)p;          // if your enum values are 7/30/90 etc.

            var clientId = await _current.GetClientIdAsync(ct);
            if (clientId is null) return new List<ThemesCompanyDto>();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // 1) Tracked companies (same filter model as other tabs)
            var tc = db.TrackedCompanies.AsNoTracking().Where(t => t.ClientId == clientId);

            if (groupId is not null)
                tc = tc.Where(t => t.TrackedCompanyGroups.Any(g => g.CompanyGroupId == groupId.Value));

            if (companyId is not null)
                tc = tc.Where(t => t.CompanyId == companyId.Value);

            /*if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLowerInvariant();
                tc = tc.Where(t => (t.Company.Name ?? "").ToLower().Contains(term) ||
                                   (t.Company.Url ?? "").ToLower().Contains(term));
            }*/

            var tracked = await tc
                .Select(t => new { t.CompanyId, Company = t.Company.Name, Url = t.Company.Url })
                .Distinct()
                .ToListAsync(ct);

            if (tracked.Count == 0)
                return new List<ThemesCompanyDto>();

            var companyIds = tracked.Select(x => x.CompanyId).ToList();
            var cutoff = DateTime.UtcNow.AddDays(-periodDays);

            // 2) Pull theme instances within period
            // Period based on the SummarizedInfo date (or gist generated time) just like Sources tab.
            var baseQuery =
                from th in db.SummarizedInfoThemes.AsNoTracking()
                join si in db.SummarizedInfos.AsNoTracking()
                    on th.SummarizedInfoId equals si.Id
                join ctg in db.CanonicalThemes.AsNoTracking()
                    on th.CanonicalThemeId equals ctg.Id into ctgLeft
                from ctg in ctgLeft.DefaultIfEmpty()
                where companyIds.Contains(si.CompanyId)
                   && ((si.Date ?? si.GistGeneratedAt ?? DateTime.MinValue) >= cutoff)
                select new
                {
                    si.CompanyId,
                    si.OriginType,
                    th.CanonicalThemeId,
                    CanonicalName = ctg != null ? ctg.Name : null,
                    FallbackLabel = th.Label,
                    th.Reason,
                    th.UpdatedAt
                };

            // Optional full-text filter on the theme label/reason (aligns with q behavior)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                baseQuery = baseQuery.Where(x =>
                    (x.CanonicalName ?? x.FallbackLabel).Contains(term) ||
                    x.Reason.Contains(term));
            }

            // Origin filter
            if (!string.IsNullOrWhiteSpace(orig) && !orig.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (orig.Equals("company", StringComparison.OrdinalIgnoreCase))
                    baseQuery = baseQuery.Where(x => x.OriginType == OriginTypeEnum.CompanyGenerated);
                else if (orig.Equals("user", StringComparison.OrdinalIgnoreCase))
                    baseQuery = baseQuery.Where(x => x.OriginType == OriginTypeEnum.UserGenerated);
            }

            var rows = await baseQuery.ToListAsync(ct);

            // 3) Group per (company, theme) and build ThemeRow
            var grouped = rows
                .GroupBy(x => new { x.CompanyId, x.CanonicalThemeId, Label = x.CanonicalName ?? x.FallbackLabel });

            var perCompany = grouped
                .GroupBy(g => g.Key.CompanyId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(grp =>
                    {
                        var latest = grp.OrderByDescending(z => z.UpdatedAt).First();
                        return new ThemeRow
                        {
                            CanonicalThemeId = grp.Key.CanonicalThemeId,
                            Label = PrettyLabel(grp.Key.Label),
                            Reason = latest.Reason,
                            Count = grp.Count(),
                            UpdatedAt = latest.UpdatedAt
                        };
                    })
                    .OrderByDescending(r => r.Count)
                    .ThenBy(r => r.Label)
                    .Take(Math.Max(1, take))
                    .ToList()
                );

            // 4) Final response
            var result = tracked
                .OrderBy(x => x.Company)
                .Select(t => new ThemesCompanyDto
                {
                    CompanyId = t.CompanyId,
                    Company = t.Company ?? "(Unknown)",
                    Url = t.Url ?? "",
                    Themes = perCompany.TryGetValue(t.CompanyId, out var list) ? list : new List<ThemeRow>()
                })
                .ToList();

            return result;
        }

        public async Task<List<TagsCompanyDto>> GetTagsAsync(
            Period p,
            int? groupId,
            int? companyId,
            string? q,
            string sent = "all",
            string orig = "all",
            int take = 10,
            CancellationToken ct = default)
        {
            var periodDays = (int)p;          // if your enum values are 7/30/90 etc.

            var clientId = await _current.GetClientIdAsync(ct);
            if (clientId is null) return new List<TagsCompanyDto>();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // 1) Tracked companies
            var tc = db.TrackedCompanies.AsNoTracking().Where(t => t.ClientId == clientId);
            if (groupId is not null)
                tc = tc.Where(t => t.TrackedCompanyGroups.Any(g => g.CompanyGroupId == groupId.Value));
            if (companyId is not null)
                tc = tc.Where(t => t.CompanyId == companyId.Value);

            var tracked = await tc
                .Select(t => new { t.CompanyId, Company = t.Company.Name, Url = t.Company.Url })
                .Distinct()
                .ToListAsync(ct);
            if (tracked.Count == 0)
                return new List<TagsCompanyDto>();

            var companyIds = tracked.Select(x => x.CompanyId).ToList();
            var cutoff = DateTime.UtcNow.AddDays(-periodDays);

            // 2) Base rows: join Tags to SummarizedInfos (for date + sentiment)
            var baseQuery =
                from tag in db.SummarizedInfoTags.AsNoTracking()
                join si in db.SummarizedInfos.AsNoTracking()
                    on tag.SummarizedInfoId equals si.Id
                where companyIds.Contains(si.CompanyId)
                   && ((si.Date ?? si.GistGeneratedAt ?? DateTime.MinValue) >= cutoff)
                select new
                {
                    si.CompanyId,
                    si.OriginType,
                    si.Sentiment,
                    Label = tag.Label,
                    Reason = (string?)tag.Reason,     // if Reason not present on your entity, swap for null or another field
                    tag.UpdatedAt
                };

            // 3) Sentiment filter
            if (!string.IsNullOrWhiteSpace(sent) && !string.Equals(sent, "all", StringComparison.OrdinalIgnoreCase))
            {
                var s = sent.ToLowerInvariant();
                if (s == "pos") baseQuery = baseQuery.Where(x => x.Label.StartsWith("+"));
                else if (s == "neg") baseQuery = baseQuery.Where(x => x.Label.StartsWith("-"));
                else if (s == "neu") baseQuery = baseQuery.Where(x => !x.Label.StartsWith("+") && !x.Label.StartsWith("-"));
            }

            // Optional q filter over label/reason
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                baseQuery = baseQuery.Where(x => x.Label.Contains(term) || (x.Reason != null && x.Reason.Contains(term)));
            }

            // Origin filter
            if (!string.IsNullOrWhiteSpace(orig) && !orig.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (orig.Equals("company", StringComparison.OrdinalIgnoreCase))
                    baseQuery = baseQuery.Where(x => x.OriginType == OriginTypeEnum.CompanyGenerated);
                else if (orig.Equals("user", StringComparison.OrdinalIgnoreCase))
                    baseQuery = baseQuery.Where(x => x.OriginType == OriginTypeEnum.UserGenerated);
            }

            var rows = await baseQuery.ToListAsync(ct);

            // 4) Group by (company, label)
            var grouped = rows
                .GroupBy(x => new { x.CompanyId, x.Label });

            var perCompany = grouped
                .GroupBy(g => g.Key.CompanyId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(grp =>
                    {
                        var latest = grp.OrderByDescending(z => z.UpdatedAt).First();
                        var raw = grp.Key.Label ?? "";
                        var sent = raw.StartsWith("+") ? "pos" : raw.StartsWith("-") ? "neg" : "neu";
                        return new TagRow
                        {
                            Label = PrettyLabel(raw),      // your prefix-aware TitleCase
                            Reason = latest.Reason,
                            Count = grp.Count(),
                            UpdatedAt = latest.UpdatedAt,
                            Sentiment = sent
                        };
                    })
                    .OrderByDescending(r => r.Count)
                    .ThenBy(r => r.Label)
                    .Take(Math.Max(1, take))
                    .ToList()
                );

            // 5) Final response
            var result = tracked
                .OrderBy(x => x.Company)
                .Select(t => new TagsCompanyDto
                {
                    CompanyId = t.CompanyId,
                    Company = t.Company ?? "(Unknown)",
                    Url = t.Url ?? "",
                    Tags = perCompany.TryGetValue(t.CompanyId, out var list) ? list : new List<TagRow>()
                })
                .ToList();

            return result;
        }

        public async Task<PagedResult<RawSignalRow>> GetRawSignalsAsync(
            Period p,
            int? groupId,
            int? companyId,
            string? q,
            string sent,
            string orig,
            IEnumerable<int>? sourceTypeIds,
            int page,
            int pageSize,
            string sortBy,
            bool desc,
            CancellationToken ct = default)
        {
            try
            {
                var periodDays = (int)p;

                var clientId = await _current.GetClientIdAsync(ct);
                if (clientId is null) return new();

                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var tcs = db.TrackedCompanies.AsNoTracking().Where(t => t.ClientId == clientId);
                if (groupId is not null) tcs = tcs.Where(t => t.TrackedCompanyGroups.Any(g => g.CompanyGroupId == groupId.Value));
                if (companyId is not null) tcs = tcs.Where(t => t.CompanyId == companyId.Value);

                var tracked = await tcs
                    .Select(t => new { t.CompanyId, Name = t.Company.Name, Url = t.Company.Url })
                    .Distinct()
                    .ToListAsync(ct);

                if (tracked.Count == 0) return new();

                var companyIds = tracked.Select(x => x.CompanyId).ToList();
                var cutoff = DateTime.UtcNow.AddDays(-periodDays);

                // Base: SummarizedInfos (primary) + join RawContent (left)
                var siq =
                    from si in db.SummarizedInfos.AsNoTracking()
                    where companyIds.Contains(si.CompanyId)
                       && ((si.Date ?? si.GistGeneratedAt ?? DateTime.MinValue) >= cutoff)
                    join rc in db.RawContents.AsNoTracking()
                        on si.RawContentId equals rc.Id into rcLeft
                    from rc in rcLeft.DefaultIfEmpty()
                    select new SignalQueryRow
                    {
                        CompanyId = si.CompanyId,
                        Date = (si.Date ?? si.GistGeneratedAt ?? rc.PostedDate ?? rc.CreatedAt) ?? DateTime.MinValue,
                        SourceTypeId = si.SourceTypeId ?? rc.DataSourceTypeId,
                        OriginType = si.OriginType,
                        PostUrl = rc != null ? rc.PostUrl : null,
                        EngagementScore = rc != null ? (rc.EngagementScore ?? 0) : 0,
                        Gist = si.Gist,
                        Sentiment = si.Sentiment,
                        SignalScore = si.SignalScore,
                        GistPointsJson = si.GistPointsJson,
                        SummarizedInfoId = si.Id              // int -> fits into int?
                    };

                var rawOnly =
                    from rc in db.RawContents.AsNoTracking()
                    where companyIds.Contains(rc.CompanyId)
                       && ((rc.PostedDate ?? rc.CreatedAt) >= cutoff)
                    join si2 in db.SummarizedInfos.AsNoTracking()
                        on rc.Id equals si2.RawContentId into si2Left
                    from si2 in si2Left.DefaultIfEmpty()
                    where si2 == null
                    select new SignalQueryRow
                    {
                        CompanyId = rc.CompanyId,
                        Date = (rc.PostedDate ?? rc.CreatedAt) ?? DateTime.MinValue,
                        SourceTypeId = rc.DataSourceTypeId,
                        OriginType = rc.OriginType,
                        PostUrl = rc.PostUrl,
                        EngagementScore = rc.EngagementScore ?? 0,
                        Gist = null,
                        Sentiment = null,
                        SignalScore = 0,
                        GistPointsJson = null,
                        SummarizedInfoId = null
                    };

                var union = siq.Concat(rawOnly);

                // Filters (search text across gist/url/source display)
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var term = q.Trim();
                    union = union.Where(x =>
                        (x.Gist != null && x.Gist.Contains(term)) ||
                        (x.PostUrl != null && x.PostUrl.Contains(term)));
                }

                // Sentiment filter (on summarized info sentiment)
                if (!string.IsNullOrWhiteSpace(sent) && !sent.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    union = sent.ToLowerInvariant() switch
                    {
                        "pos" => union.Where(x => x.Sentiment == SentimentEnum.Positive),
                        "neu" => union.Where(x => x.Sentiment == SentimentEnum.Neutral),
                        "neg" => union.Where(x => x.Sentiment == SentimentEnum.Negative),
                        _ => union
                    };
                }

                // Origin filter
                if (!string.IsNullOrWhiteSpace(orig) && !orig.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    if (orig.Equals("company", StringComparison.OrdinalIgnoreCase))
                        union = union.Where(x => x.OriginType == OriginTypeEnum.CompanyGenerated);
                    else if (orig.Equals("user", StringComparison.OrdinalIgnoreCase))
                        union = union.Where(x => x.OriginType == OriginTypeEnum.UserGenerated);
                }

                // Source filter
                var sourceArr = sourceTypeIds?.Distinct().ToArray();
                if (sourceArr is { Length: > 0 })
                    union = union.Where(x => x.SourceTypeId.HasValue && sourceArr.Contains(x.SourceTypeId.Value));

                // Count total
                var total = await union.CountAsync(ct);

                // Sort
                var key = (sortBy ?? "date").ToLowerInvariant();
                union = key switch
                {
                    "company" => (desc ? union.OrderByDescending(x => x.CompanyId) : union.OrderBy(x => x.CompanyId)),
                    "source" => (desc ? union.OrderByDescending(x => x.SourceTypeId) : union.OrderBy(x => x.SourceTypeId)),
                    "origin" => (desc ? union.OrderByDescending(x => x.OriginType) : union.OrderBy(x => x.OriginType)),
                    "eng" => (desc ? union.OrderByDescending(x => x.EngagementScore) : union.OrderBy(x => x.EngagementScore)),
                    "score" => (desc ? union.OrderByDescending(x => x.SignalScore) : union.OrderBy(x => x.SignalScore)),
                    _ => (desc ? union.OrderByDescending(x => x.Date) : union.OrderBy(x => x.Date)),
                };


                // Page
                union = union.Skip(page * pageSize).Take(pageSize);

                // Materialize
                var rows = await union.ToListAsync(ct);

                // Preload related multi-values for the materialized summarized infos
                var siIds = rows.Select(r => r.SummarizedInfoId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

                var themeMap = new Dictionary<int, List<ReasonItem>>();
                var tagMap = new Dictionary<int, List<ReasonItem>>();

                if (siIds.Count > 0)
                {
                    var themes = await db.SummarizedInfoThemes.AsNoTracking()
                        .Where(t => siIds.Contains(t.SummarizedInfoId))
                        .Include(t => t.CanonicalTheme)
                        .Select(t => new { t.SummarizedInfoId, Label = t.CanonicalTheme != null ? t.CanonicalTheme.Name : t.Label, t.Reason })
                        .ToListAsync(ct);

                    themeMap = themes
                        .GroupBy(x => x.SummarizedInfoId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => new ReasonItem { Label = PrettyLabel(x.Label), Reason = x.Reason }).ToList()
                        );

                    var tags = await db.SummarizedInfoTags.AsNoTracking()
                        .Where(t => siIds.Contains(t.SummarizedInfoId))
                        .Select(t => new { t.SummarizedInfoId, t.Label, t.Reason })
                        .ToListAsync(ct);

                    tagMap = tags
                        .GroupBy(x => x.SummarizedInfoId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => new ReasonItem { Label = PrettyLabel(x.Label), Reason = x.Reason }).ToList()
                        );
                }

                // Map
                var result = new PagedResult<RawSignalRow> { Total = total };
                foreach (var r in rows)
                {
                    var trackedCompany = tracked.FirstOrDefault(t => t.CompanyId == r.CompanyId);
                    var sourceName = r.SourceTypeId.HasValue ? SourceDisplay((DataSourceTypeEnum)r.SourceTypeId.Value) : "";

                    result.Items.Add(new RawSignalRow
                    {
                        CompanyId = r.CompanyId,
                        Company = trackedCompany?.Name ?? "(Unknown)",
                        Url = r.PostUrl ?? "",
                        Date = r.Date,
                        Source = sourceName,
                        SourceTypeId = r.SourceTypeId,
                        Origin = r.OriginType == OriginTypeEnum.CompanyGenerated ? "company" : "user",
                        EngagementScore = r.EngagementScore,

                        Gist = r.Gist,
                        Sentiment = r.Sentiment switch
                        {
                            SentimentEnum.Positive => "pos",
                            SentimentEnum.Neutral => "neu",
                            SentimentEnum.Negative => "neg",
                            _ => null
                        },
                        SignalScore = r.SignalScore,
                        GistPoints = ParsePoints(r.GistPointsJson),

                        Themes = (r.SummarizedInfoId is int sid && themeMap.TryGetValue(sid, out var tl)) ? tl : new(),
                        Tags = (r.SummarizedInfoId is int sid2 && tagMap.TryGetValue(sid2, out var tg)) ? tg : new()
                    });
                }

                return result;

                static List<string> ParsePoints(string? json)
                {
                    if (string.IsNullOrWhiteSpace(json)) return new();
                    try
                    {
                        var arr = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                        return arr?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new();
                    }
                    catch { return new(); }
                }

            }
            catch (OperationCanceledException)
            {
                // expected when UI cancels in-flight load (search/filter changes)
                return new PagedResult<RawSignalRow>();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (
                ex.Message.Contains("Operation cancelled by user", StringComparison.OrdinalIgnoreCase))
            {
                // SQL Server reports client-side cancel this way
                return new PagedResult<RawSignalRow>();
            }
        }

        private static string PrettyLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            // keep non-alphanumeric prefix (e.g., "+", "-")
            int i = 0; while (i < raw.Length && !char.IsLetterOrDigit(raw[i])) i++;
            var prefix = raw[..i];
            var core = raw[i..].Replace('_', ' ').Replace('-', ' ');

            var parts = core.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int k = 0; k < parts.Length; k++)
            {
                var p = parts[k];

                // Preserve acronyms (letters-only are ALL CAPS) and mixed-case tokens (SaaS, iPhone)
                bool hasLetters = p.Any(char.IsLetter);
                bool allLettersUpper = hasLetters && p.Where(char.IsLetter).All(char.IsUpper);
                bool hasUpperBeyondFirst = p.Length > 1 && p.Skip(1).Any(char.IsUpper);
                if (allLettersUpper || hasUpperBeyondFirst)
                {
                    parts[k] = p;
                    continue;
                }

                // Also handle slashes inside a token if present: ppc/sem -> Ppc/Sem
                if (p.Contains('/'))
                {
                    parts[k] = string.Join('/', p.Split('/').Select(TitleOne));
                }
                else
                {
                    parts[k] = TitleOne(p);
                }
            }

            return prefix + string.Join(' ', parts);

            static string TitleOne(string s) =>
                s.Length switch
                {
                    0 => s,
                    1 => char.ToUpperInvariant(s[0]).ToString(),
                    _ => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant()
                };
        }


        // Friendly display names for the enum
        private static string SourceDisplay(DataSourceTypeEnum t) => t switch
        {
            DataSourceTypeEnum.X => "X / Twitter",
            DataSourceTypeEnum.Youtube => "YouTube",
            DataSourceTypeEnum.EmailNewsletters => "Email newsletters",
            DataSourceTypeEnum.G2 => "G2",
            DataSourceTypeEnum.TrustRadius => "TrustRadius",
            DataSourceTypeEnum.GetApp => "GetApp",
            DataSourceTypeEnum.SoftwareAdvice => "Software Advice",
            DataSourceTypeEnum.GartnerPeerInsights => "Gartner Peer Insights",
            DataSourceTypeEnum.Linkedin => "LinkedIn",
            DataSourceTypeEnum.Reddit => "Reddit",
            DataSourceTypeEnum.FacebookReviews => "Facebook Reviews",
            DataSourceTypeEnum.CompanyContent => "Company content",
            DataSourceTypeEnum.Blog => "Blog",
            _ => t.ToString()
        };

        // ---------- helpers ----------
        private static List<List<string>> ParseEvidence(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try
            {
                var arr = System.Text.Json.JsonSerializer.Deserialize<List<List<string>>>(json);
                if (arr is not null)
                    return arr.Where(g => g?.Count > 0)
                              .Select(g => g.Where(s => !string.IsNullOrWhiteSpace(s)).ToList())
                              .ToList();
            }
            catch { }

            try
            {
                var flat = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (flat is not null) return new() { flat.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() };
            }
            catch { }

            return new() { new() { json } };
        }
    }
}
