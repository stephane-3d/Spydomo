using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Constants;
using Spydomo.Common.Enums;
using Spydomo.DTO.MarketPulse;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Globalization;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class MarketPulseGenerator : IMarketPulseGenerator
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public MarketPulseGenerator(IDbContextFactory<SpydomoContext> dbFactory)
            => _dbFactory = dbFactory;

        public async Task<MarketPulseViewModel> GenerateAsync(CompanyGroup group, int timeWindowDays, CancellationToken ct)
        {
            List<RelatedGroupLinkDto> relatedGroups;
            const int SeoClientId = 2; // steph

            var since = DateTime.UtcNow.AddDays(-timeWindowDays);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // ✅ Related groups: 3 random public groups from your own account (clientId=2, isPrivate=0)
            relatedGroups = await db.CompanyGroups
                .AsNoTracking()
                .Where(g =>
                    g.ClientId == SeoClientId &&
                    !g.IsPrivate &&
                    g.Id != group.Id &&
                    g.Slug != null &&
                    g.Name != null)
                .OrderBy(_ => Guid.NewGuid()) // SQL Server -> ORDER BY NEWID()
                .Select(g => new RelatedGroupLinkDto
                {
                    Name = g.Name,
                    Slug = g.Slug
                })
                .Take(3)
                .ToListAsync(ct);

            // 1) companies in this group (global CompanyIds)
            var companyIds = await db.TrackedCompanyGroups.AsNoTracking()
                .Where(x => x.CompanyGroupId == group.Id)
                .Select(x => x.TrackedCompany.CompanyId)
                .Distinct()
                .ToListAsync(ct);

            if (companyIds.Count == 0)
                return new MarketPulseViewModel { Companies = new() };

            // 2) recent summaries for those companies in this group
            var summaries = await db.StrategicSummaries.AsNoTracking()
                .Where(s => s.CompanyGroupId == group.Id)
                .Where(s => s.CompanyId != null && companyIds.Contains(s.CompanyId.Value))
                .Where(s => s.CreatedOn >= since)
                .Select(s => new
                {
                    s.Id,
                    CompanyId = s.CompanyId!.Value,
                    s.SummaryText,
                    s.Tier,
                    s.TierReason,
                    s.SummarizedInfoId,
                    s.CreatedOn,
                    s.IncludedSignalTypes
                })
                .ToListAsync(ct);

            // 3) featured summary per company
            var featuredByCompany = summaries
                .GroupBy(s => s.CompanyId)
                .Select(g => PickFeatured(g))
                .Where(x => x != null)
                .ToDictionary(x => x!.CompanyId, x => x!);

            var featuredSummarizedInfoIds = featuredByCompany.Values
                .Select(x => x.SummarizedInfoId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            // 4) Get company basics (name/slug/positioning)
            var companies = await db.Companies.AsNoTracking()
                .Where(c => companyIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Slug,
                    // adjust to your schema:
                    PositioningLine = c.SelfDescription ?? "",
                    PositioningTitle = c.SelfPositioning ?? c.SelfTitle ?? ""
                })
                .ToListAsync(ct);

            // 5) themes/tags from all recent summarized infos (not just featured)
            var summarizedIds = summaries
                .Select(s => s.SummarizedInfoId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var themes = await db.SummarizedInfoThemes.AsNoTracking()
                .Where(t => summarizedIds.Contains(t.SummarizedInfoId))
                .Select(t => new { t.SummarizedInfoId, Label = t.CanonicalThemeId != null ? t.CanonicalTheme.Name : t.Label })
                .ToListAsync(ct);

            var tags = await db.SummarizedInfoTags.AsNoTracking()
                .Where(t => summarizedIds.Contains(t.SummarizedInfoId))
                .Select(t => new { t.SummarizedInfoId, Label = t.CanonicalTagId != null ? t.CanonicalTag!.Name : t.Label })
                .ToListAsync(ct);

            // 6) map summarizedInfoId -> companyId (from summaries)
            var siToCompany = summaries
                .Where(s => s.SummarizedInfoId.HasValue)
                .GroupBy(s => s.SummarizedInfoId!.Value)
                .ToDictionary(g => g.Key, g => g.First().CompanyId);

            // 7) aggregate top themes/tags per company
            var topThemesByCompany = themes
                .Where(x => siToCompany.ContainsKey(x.SummarizedInfoId))
                .GroupBy(x => siToCompany[x.SummarizedInfoId])
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => CleanLabel(x.Label))
                          .OrderByDescending(gg => gg.Count())
                          .Take(3)
                          .Select(gg => PrettyLabel(gg.Key))
                          .ToList()
                );

            var topTagsByCompany = tags
                .Where(x => siToCompany.ContainsKey(x.SummarizedInfoId))
                .GroupBy(x => siToCompany[x.SummarizedInfoId])
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => CleanLabel(x.Label))
                          .OrderByDescending(gg => gg.Count())
                          .Take(3)
                          .Select(gg => PrettyLabel(gg.Key))
                          .ToList()
                );

            // 8) build cards
            var cards = new List<MarketPulseCompanyCard>();

            foreach (var c in companies.OrderBy(x => x.Name))
            {
                if (!featuredByCompany.TryGetValue(c.Id, out var f))
                    continue;

                cards.Add(new MarketPulseCompanyCard
                {
                    CompanyId = c.Id,
                    CompanyName = c.Name,
                    CompanySlug = c.Slug,
                    PositioningLine = c.PositioningLine,
                    PositioningTitle = c.PositioningTitle,

                    Signal = f.SummaryText,
                    WhyItMatters = CleanWhy(f.TierReason),

                    SignalTypes = f.SignalTypeSlugs.Take(2).Select(x => x.ToString()).ToList(),
                    Themes = topThemesByCompany.TryGetValue(c.Id, out var th) ? th.Take(3).ToList() : new(),
                    Tags = topTagsByCompany.TryGetValue(c.Id, out var tg) ? tg.Take(3).ToList() : new(),
                });
            }

            return new MarketPulseViewModel
            {
                Companies = cards,
                RelatedGroups = relatedGroups
            };
        }

        private static Featured? PickFeatured(IEnumerable<dynamic> items)
        {
            // 1) tier order
            static int TierScore(PulseTier? t) => t switch
            {
                PulseTier.Tier1 => 3,
                PulseTier.Tier2 => 2,
                PulseTier.Tier3 => 1,
                _ => 0
            };

            // 2) signal type preference
            static int TypeBonus(IReadOnlyCollection<string> slugs)
            {
                if (slugs is null || slugs.Count == 0) return 0;

                bool Has(string slug) => slugs.Contains(slug, StringComparer.OrdinalIgnoreCase);

                var bonus = 0;
                if (Has(SignalSlugs.FeatureLaunch)) bonus += 3;
                if (Has(SignalSlugs.StrategicMove)) bonus += 2;
                if (Has(SignalSlugs.PositioningPlay)) bonus += 2;
                if (Has(SignalSlugs.SocialProofDrop)) bonus += 1;
                return bonus;
            }

            var best = items
                .Select(s => new
                {
                    CompanyId = (int)s.CompanyId,
                    s.SummaryText,
                    s.TierReason,
                    s.SummarizedInfoId,
                    s.CreatedOn,
                    SignalTypeSlugs = s.IncludedSignalTypes ?? new List<string>(),
                    Score = (TierScore((PulseTier?)s.Tier) * 100)
                          + TypeBonus(s.IncludedSignalTypes ?? new List<string>())
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.CreatedOn)
                .FirstOrDefault();

            if (best == null) return null;

            return new Featured
            {
                CompanyId = best.CompanyId,
                SummaryText = best.SummaryText ?? "",
                TierReason = best.TierReason ?? "",
                SummarizedInfoId = (int?)best.SummarizedInfoId,
                SignalTypeSlugs = best.SignalTypeSlugs ?? new List<string>()
            };
        }

        private sealed class Featured
        {
            public int CompanyId { get; set; }
            public string SummaryText { get; set; } = "";
            public string? TierReason { get; set; }
            public int? SummarizedInfoId { get; set; }
            public List<string> SignalTypeSlugs { get; set; } = new();
        }

        private static string CleanWhy(string? s)
            => string.IsNullOrWhiteSpace(s) ? "A notable signal this period." : s.Trim().TrimEnd('.');

        private static string CleanLabel(string s)
            => (s ?? "").Trim().TrimStart('+').ToLowerInvariant();

        private static string PrettyLabel(string s)
            => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((s ?? "").Replace("_", " "));
    }

}
