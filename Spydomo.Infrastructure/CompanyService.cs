using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.DTO;
using Spydomo.Infrastructure.BackgroundServices;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Models.Extensions;
using Spydomo.Utilities;

namespace Spydomo.Infrastructure
{
    public class CompanyService : ICompanyService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly CompanyDataService _companyDataService;
        private readonly IWorkerAdminClient _workerAdminClient;
        private readonly ILogger<CompanyService> _logger;

        public CompanyService(
            IDbContextFactory<SpydomoContext> dbFactory,
            CompanyDataService companyDataService,
            IWorkerAdminClient workerAdminClient,
            ILogger<CompanyService> logger)
        {
            _dbFactory = dbFactory;
            _companyDataService = companyDataService;
            _workerAdminClient = workerAdminClient;
            _logger = logger;
        }

        public async Task<TrackedCompanyDto> AddOrGetTrackedCompanyAsync(
    int clientId,
    string inputUrl,
    CancellationToken ct = default)
        {
            var normalizedUrl = UrlHelper.ExtractDomainFromUrl(inputUrl); // host-only
            var slug = SlugHelper.GenerateSlug(inputUrl);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // 1) Find Company (prefer Url)
            var company = await db.Companies
                .FirstOrDefaultAsync(c => c.Url == normalizedUrl, ct);

            if (company == null)
            {
                // optional fallback
                company = await db.Companies.FirstOrDefaultAsync(c => c.Slug == slug, ct);
            }

            // 1b) Create if missing
            if (company == null)
            {
                var isReachable = await UrlHelper.UrlExistsAsync(UrlHelper.GetHttpsUrl(normalizedUrl));
                if (!isReachable)
                    throw new Exception("URL is not reachable.");

                company = new Company
                {
                    Url = normalizedUrl,
                    Slug = slug,
                    Name = UrlHelper.ExtractDomainFromUrl(inputUrl),
                    DateCreated = DateTime.UtcNow,
                    Status = "NEW"
                };

                db.Companies.Add(company);
                await db.SaveChangesAsync(ct); // need Id

                try
                {
                    await _workerAdminClient.EnqueueCompanyDataAsync(company.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enqueue company data for {CompanyId}", company.Id);
                }
            }
            else
            {
                // 1c) Promote DISCOVERED stub → NEW (and enqueue CompanyData once)
                if (string.Equals(company.Status, "DISCOVERED", StringComparison.OrdinalIgnoreCase))
                {
                    company.Status = "NEW";
                    company.LastCompanyDataUpdate = null; // optional
                    await db.SaveChangesAsync(ct);

                    try
                    {
                        await _workerAdminClient.EnqueueCompanyDataAsync(company.Id, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enqueue company data for discovered company {CompanyId}", company.Id);
                    }
                }
            }

            // 2) If already tracked, return (projection DTO)
            var trackedDto = await db.TrackedCompanies
                .AsNoTracking()
                .Where(tc => tc.ClientId == clientId && tc.CompanyId == company.Id)
                .Select(tc => new TrackedCompanyDto
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Notes = tc.Notes,
                    DateCreated = tc.DateCreated,
                    CompanyId = tc.CompanyId,
                    CompanyName = tc.Company.Name,
                    CompanyUrl = tc.Company.Url,
                    Status = tc.Company.Status
                })
                .FirstOrDefaultAsync(ct);

            if (trackedDto != null)
                return trackedDto;

            // 3) Get default group id
            var defaultSlug = $"default-{clientId}";
            var defaultGroup = await db.CompanyGroups
                .FirstOrDefaultAsync(g => g.ClientId == clientId && g.Slug == defaultSlug, ct);

            if (defaultGroup == null)
                throw new Exception("Default group is missing for this client.");

            // Warmup data for this company for this client
            try
            {
                await _workerAdminClient.EnqueueWarmupAsync(clientId, company.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue warmup for client {ClientId} company {CompanyId}", clientId, company.Id);
            }

            // 4) Create TrackedCompany + link row + increment count
            var trackedCompany = new TrackedCompany
            {
                ClientId = clientId,
                Name = company.Name,
                CompanyId = company.Id,
                DateCreated = DateTime.UtcNow
            };

            db.TrackedCompanies.Add(trackedCompany);

            db.TrackedCompanyGroups.Add(new TrackedCompanyGroup
            {
                TrackedCompany = trackedCompany,
                CompanyGroupId = defaultGroup.Id
            });

            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
            if (client != null)
                client.TrackedCompaniesCount = client.TrackedCompaniesCount + 1;

            await db.SaveChangesAsync(ct);

            return trackedCompany.ToDto();
        }

        public async Task<List<TrackedCompanyDto>> GetTrackedCompaniesForClientAsync(
            int clientId,
            int? groupId = null,
            CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var q = db.TrackedCompanies
                .AsNoTracking()
                .Where(tc => tc.ClientId == clientId);

            if (groupId.HasValue)
            {
                var gid = groupId.Value;
                q = q.Where(tc => tc.TrackedCompanyGroups.Any(x => x.CompanyGroupId == gid));
            }

            return await q
                .OrderByDescending(tc => tc.DateCreated)
                .Select(tc => new TrackedCompanyDto
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Notes = tc.Notes,
                    DateCreated = tc.DateCreated,

                    CompanyId = tc.Company.Id,
                    CompanyName = tc.Company.Name,
                    CompanyUrl = tc.Company.Url,
                    Status = tc.Company.Status
                })
                .ToListAsync(ct);
        }


        public async Task UpdateTrackedCompanyNoteAsync(int trackedCompanyId, string note, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var tracked = await db.TrackedCompanies.FindAsync(trackedCompanyId);

            if (tracked == null)
                throw new Exception("Tracked company not found.");

            tracked.Notes = note;
            await db.SaveChangesAsync();
        }
        public async Task RemoveTrackedCompanyAsync(int clientId, int trackedCompanyId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            // Load with join rows (and enforce client ownership)
            var tracked = await db.TrackedCompanies
                .Include(tc => tc.TrackedCompanyGroups)
                .FirstOrDefaultAsync(tc => tc.Id == trackedCompanyId && tc.ClientId == clientId);

            if (tracked == null)
                throw new Exception("Tracked company not found.");

            // Remove join rows first
            if (tracked.TrackedCompanyGroups.Count > 0)
                db.TrackedCompanyGroups.RemoveRange(tracked.TrackedCompanyGroups);

            db.TrackedCompanies.Remove(tracked);

            var client = await db.Clients.FindAsync(clientId);
            if (client != null && client.TrackedCompaniesCount > 0)
                client.TrackedCompaniesCount--;

            await db.SaveChangesAsync();
        }


    }

}
