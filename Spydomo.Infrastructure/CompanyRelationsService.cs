using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;

namespace Spydomo.Infrastructure
{
    public sealed class CompanyRelationsService : ICompanyRelationsService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public CompanyRelationsService(IDbContextFactory<SpydomoContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<int> CreateRunAsync(int companyId, string query, string promptVersion, string rawJson, string parsedJson, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var run = new CompanyRelationRun
            {
                CompanyId = companyId,
                Query = query,
                PromptVersion = promptVersion,
                RawResponseJson = rawJson,
                ParsedCandidatesJson = parsedJson,
                Provider = CompanyRelationSource.Perplexity,
                Success = true,
                CreatedAt = DateTime.UtcNow
            };

            db.CompanyRelationRuns.Add(run);
            await db.SaveChangesAsync(ct);
            return run.Id;
        }

        public async Task UpsertCandidatesAsync(int companyId, int runId, IEnumerable<CompanyCandidateDto> candidates, CompanyRelationSource source, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            foreach (var c in candidates)
            {
                var domain = UrlHelper.ExtractDomainFromUrl(c.Url);

                // If we can already resolve by domain, do it now
                int? relatedCompanyId = null;
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    relatedCompanyId = await db.Companies
                        .AsNoTracking()
                        .Where(x => x.Url != null && x.Url.Contains(domain))
                        .Select(x => (int?)x.Id)
                        .FirstOrDefaultAsync(ct);
                }

                if (relatedCompanyId.HasValue)
                {
                    var existing = await db.CompanyRelations
                        .FirstOrDefaultAsync(x =>
                            x.CompanyId == companyId &&
                            x.RelatedCompanyId == relatedCompanyId.Value &&
                            x.RelationType == c.RelationType, ct);

                    if (existing is null)
                    {
                        db.CompanyRelations.Add(new CompanyRelation
                        {
                            CompanyId = companyId,
                            RelatedCompanyId = relatedCompanyId.Value,
                            RelationType = c.RelationType,
                            Source = source,
                            Confidence = c.Confidence,
                            EvidenceCount = 1,
                            FirstSeenAt = now,
                            LastSeenAt = now,
                            RunId = runId,
                            Reason = c.Reason
                        });
                    }
                    else
                    {
                        existing.EvidenceCount += 1;
                        existing.LastSeenAt = now;
                        existing.Confidence = Math.Max(existing.Confidence, c.Confidence);
                        existing.RunId = runId;
                        existing.Reason ??= c.Reason;
                        if (existing.Status == CompanyRelationStatus.Rejected)
                            existing.Status = CompanyRelationStatus.Proposed;
                    }
                }
                else
                {
                    // unresolved: store raw + domain
                    var existing = await db.CompanyRelations
                        .FirstOrDefaultAsync(x =>
                            x.CompanyId == companyId &&
                            x.RelatedCompanyId == null &&
                            x.RelatedDomain == domain &&
                            x.RelationType == c.RelationType, ct);

                    if (existing is null)
                    {
                        db.CompanyRelations.Add(new CompanyRelation
                        {
                            CompanyId = companyId,
                            RelatedCompanyId = null,
                            RelatedCompanyNameRaw = c.Name,
                            RelatedCompanyUrlRaw = c.Url,
                            RelatedDomain = domain,
                            RelationType = c.RelationType,
                            Source = source,
                            Confidence = c.Confidence,
                            EvidenceCount = 1,
                            FirstSeenAt = now,
                            LastSeenAt = now,
                            RunId = runId,
                            Reason = c.Reason
                        });
                    }
                    else
                    {
                        existing.EvidenceCount += 1;
                        existing.LastSeenAt = now;
                        existing.Confidence = Math.Max(existing.Confidence, c.Confidence);
                        existing.RelatedCompanyNameRaw ??= c.Name;
                        existing.RelatedCompanyUrlRaw ??= c.Url;
                        existing.RunId = runId;
                        existing.Reason ??= c.Reason;
                        if (existing.Status == CompanyRelationStatus.Rejected)
                            existing.Status = CompanyRelationStatus.Proposed;
                    }
                }
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task<List<CompanyRelation>> GetRelationsAsync(int companyId, CompanyRelationStatus minStatus, int take, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.CompanyRelations
                .AsNoTracking()
                .Include(x => x.RelatedCompany)
                .Where(x => x.CompanyId == companyId && x.Status >= minStatus && x.RelatedCompanyId != null)
                .OrderByDescending(x => x.Status)
                .ThenByDescending(x => x.Confidence)
                .ThenByDescending(x => x.EvidenceCount)
                .Take(take)
                .ToListAsync(ct);
        }
    }
}
