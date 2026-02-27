using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;
using Spydomo.Utilities;

namespace Spydomo.Infrastructure
{
    public sealed class CompanySuggestionService : ICompanySuggestionService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ICompanyRelationsService _relations;
        private readonly ICompanyRelationsReconciliationService _recon;
        private readonly IPerplexityCompanyLandscapeClient _perplexity;

        public CompanySuggestionService(
            IDbContextFactory<SpydomoContext> dbFactory,
            ICompanyRelationsService relations,
            ICompanyRelationsReconciliationService recon,
            IPerplexityCompanyLandscapeClient perplexity)
        {
            _dbFactory = dbFactory;
            _relations = relations;
            _recon = recon;
            _perplexity = perplexity;
        }

        public async Task<List<CompanySuggestionDto>> GetSuggestionsForNewCompanyAsync(
            int clientId,
            int seedCompanyId,
            int take = 6,
            CancellationToken ct = default)
        {
            // 1) Try without Perplexity first
            var result = await GetSuggestionsInternalAsync(clientId, seedCompanyId, take, ct);

            // 2) If weak, seed with Perplexity, store edges, reconcile, then re-fetch
            if (result.Count < Math.Min(4, take))
            {
                await SeedLandscapeIfNeededAsync(seedCompanyId, ct);
                result = await GetSuggestionsInternalAsync(clientId, seedCompanyId, take, ct);
            }

            return result;
        }

        private async Task<List<CompanySuggestionDto>> GetSuggestionsInternalAsync(
            int clientId,
            int seedCompanyId,
            int take,
            CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Exclude already tracked by this client
            var alreadyTracked = await db.TrackedCompanies
                .AsNoTracking()
                .Where(tc => tc.ClientId == clientId)
                .Select(tc => tc.CompanyId)
                .ToListAsync(ct);

            var exclude = new HashSet<int>(alreadyTracked) { seedCompanyId };

            // A) Co-occurrence (threshold >=2)
            var co = await GetCoOccurrenceAsync(db, seedCompanyId, exclude, ct);

            // B) Trusted relations
            var rel = await db.CompanyRelations
                .AsNoTracking()
                .Include(r => r.RelatedCompany)
                .Where(r => r.CompanyId == seedCompanyId
                         && r.Status == CompanyRelationStatus.Trusted
                         && r.RelatedCompanyId != null
                         && !exclude.Contains(r.RelatedCompanyId.Value))
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => r.EvidenceCount)
                .Take(30)
                .Select(r => new CompanySuggestionDto(
                    r.RelatedCompanyId!.Value,
                    r.RelatedCompany!.Name ?? "(Unknown)",
                    r.RelatedCompany!.Url,
                    "Competitive landscape",
                    (decimal)r.Confidence))
                .ToListAsync(ct);

            // Blend + dedupe
            var seen = new HashSet<int>();
            var outList = new List<CompanySuggestionDto>(take);

            void Add(IEnumerable<CompanySuggestionDto> items)
            {
                foreach (var it in items)
                {
                    if (outList.Count >= take) break;
                    if (exclude.Contains(it.CompanyId)) continue;
                    if (!seen.Add(it.CompanyId)) continue;
                    outList.Add(it);
                }
            }

            Add(co);
            Add(rel);

            // Relax if still too low: allow co-occurrence >=1
            if (outList.Count < Math.Min(3, take))
            {
                var coRelaxed = await GetCoOccurrenceAsync(db, seedCompanyId, exclude, ct, minCount: 1);
                Add(coRelaxed);
            }

            return outList;
        }

        private static async Task<List<CompanySuggestionDto>> GetCoOccurrenceAsync(
            SpydomoContext db,
            int seedCompanyId,
            HashSet<int> exclude,
            CancellationToken ct,
            int minCount = 2)
        {
            var clientsWithSeed = db.TrackedCompanies
                .AsNoTracking()
                .Where(tc => tc.CompanyId == seedCompanyId)
                .Select(tc => tc.ClientId)
                .Distinct();

            var coCounts = await db.TrackedCompanies
                .AsNoTracking()
                .Where(tc => clientsWithSeed.Contains(tc.ClientId) && tc.CompanyId != seedCompanyId)
                .GroupBy(tc => tc.CompanyId)
                .Select(g => new { CompanyId = g.Key, CoCount = g.Select(x => x.ClientId).Distinct().Count() })
                .Where(x => x.CoCount >= minCount)
                .OrderByDescending(x => x.CoCount)
                .Take(30)
                .ToListAsync(ct);

            var ids = coCounts.Select(x => x.CompanyId).Where(id => !exclude.Contains(id)).ToList();

            var companies = await db.Companies
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Url })
                .ToListAsync(ct);

            var map = coCounts.ToDictionary(x => x.CompanyId, x => x.CoCount);

            return companies
                .Select(c => new CompanySuggestionDto(
                    c.Id,
                    c.Name ?? "(Unknown)",
                    c.Url,
                    "Often tracked with",
                    Score: map.TryGetValue(c.Id, out var cnt) ? cnt : 0))
                .OrderByDescending(x => x.Score)
                .ToList();
        }

        private async Task SeedLandscapeIfNeededAsync(int seedCompanyId, CancellationToken ct)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var company = await db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == seedCompanyId, ct);

            if (company is null || string.IsNullOrWhiteSpace(company.Name) || string.IsNullOrWhiteSpace(company.Url))
                return;

            // Cooldown: don’t call Perplexity too often for same company (30 days)
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var recently = await db.CompanyRelationRuns
                .AsNoTracking()
                .AnyAsync(r => r.CompanyId == seedCompanyId && r.CreatedAt >= cutoff && r.Success, ct);

            if (recently) return;

            // Call Perplexity
            var resp = await _perplexity.GetLandscapeAsync(company.Name!, UrlHelper.GetHttpsUrl(company.Url!), limit: 10, companyId: seedCompanyId, ct: ct);

            // Store run
            var rawJson = JsonSerializer.Serialize(resp);
            var runId = await _relations.CreateRunAsync(seedCompanyId, $"landscape:{company.Url}", "v1", rawJson, rawJson, ct);

            // Map candidates -> CompanyCandidateDto
            var candidates = resp.companies
                .Where(x => !string.IsNullOrWhiteSpace(x.name) && !string.IsNullOrWhiteSpace(x.url))
                .Select(x => new CompanyCandidateDto(
                    Name: x.name,
                    Url: x.url,
                    RelationType: ParseRelationType(x.relationType),
                    Confidence: (decimal)Math.Clamp(x.confidence, 0, 1),
                    Reason: x.reason))
                .ToList();

            await _relations.UpsertCandidatesAsync(seedCompanyId, runId, candidates, CompanyRelationSource.Perplexity, ct);

            // Reconcile (link domains, auto-create missing companies, promote)
            await _recon.ReconcileAsync(seedCompanyId, ct);
        }

        private static CompanyRelationType ParseRelationType(string s)
            => s?.Trim().ToLowerInvariant() switch
            {
                "competitor" => CompanyRelationType.Competitor,
                "alternative" => CompanyRelationType.Alternative,
                "adjacent" => CompanyRelationType.Adjacent,
                _ => CompanyRelationType.SameSpace
            };
    }
}
