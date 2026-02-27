using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System;
using System.Collections.Generic;
using System.Text;
using Spydomo.Utilities;

namespace Spydomo.Infrastructure
{
    public sealed class CompanyRelationsReconciliationService : ICompanyRelationsReconciliationService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public CompanyRelationsReconciliationService(IDbContextFactory<SpydomoContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task ReconcileAsync(int companyId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            // 1) Resolve unresolved relations by domain (and optionally create missing companies)
            var unresolved = await db.CompanyRelations
                .Where(x => x.CompanyId == companyId
                         && x.RelatedCompanyId == null
                         && x.Status != CompanyRelationStatus.Rejected
                         && x.RelatedDomain != null)
                .ToListAsync(ct);

            foreach (var rel in unresolved)
            {
                var domain = rel.RelatedDomain!;
                var existingCompany = await db.Companies
                    .FirstOrDefaultAsync(c => c.Url != null && c.Url.Contains(domain), ct);

                if (existingCompany is null)
                {
                    // Auto-create a company record (directory-only)
                    existingCompany = new Company
                    {
                        Name = rel.RelatedCompanyNameRaw,
                        Url = UrlHelper.ExtractDomainFromUrl(rel.RelatedCompanyUrlRaw),
                        Slug = Slugify(rel.RelatedCompanyNameRaw ?? domain),
                        DateCreated = now,
                        IsActive = true,
                        Status = "DISCOVERED",
                        PrimaryCategoryId = await db.Companies
                            .Where(c => c.Id == companyId)
                            .Select(c => c.PrimaryCategoryId)
                            .FirstOrDefaultAsync(ct),
                        CategoryConfidence = 0.35m,
                        CategoryReason = "Inherited from seed company"
                    };

                    db.Companies.Add(existingCompany);
                    await db.SaveChangesAsync(ct); // need Id
                }

                // Link and clear raw fields (keep them if you prefer for audit)
                rel.RelatedCompanyId = existingCompany.Id;
                rel.RelatedCompanyNameRaw ??= existingCompany.Name;
                rel.RelatedCompanyUrlRaw ??= existingCompany.Url;
            }

            await db.SaveChangesAsync(ct);

            // 2) Promotion rules: Proposed -> Trusted
            // Simple rule set for v1:
            // - EvidenceCount >= 2 OR Confidence >= 0.85 -> Trusted
            // (Later you’ll add co-occurrence corroboration)
            var proposed = await db.CompanyRelations
                .Where(x => x.CompanyId == companyId
                         && x.RelatedCompanyId != null
                         && x.Status == CompanyRelationStatus.Proposed)
                .ToListAsync(ct);

            foreach (var rel in proposed)
            {
                if (rel.EvidenceCount >= 2 || rel.Confidence >= 0.85m)
                    rel.Status = CompanyRelationStatus.Trusted;
            }

            await db.SaveChangesAsync(ct);
        }

        private static string Slugify(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "company";
            var s = input.Trim().ToLowerInvariant();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9\s-]", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "-");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"-+", "-");
            return s.Trim('-');
        }
    }
}
