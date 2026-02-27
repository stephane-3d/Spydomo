using Microsoft.EntityFrameworkCore;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Models.Extensions;
using Spydomo.Utilities;

namespace Spydomo.Infrastructure
{
    public class CompanyGroupService : ICompanyGroupService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public CompanyGroupService(IDbContextFactory<SpydomoContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<CompanyGroupDto>> GetCompanyGroupsForClientAsync(int clientId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            return await db.CompanyGroups
                .Where(g => g.ClientId == clientId)
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => new CompanyGroupDto
                {
                    Id = g.Id,
                    ClientId = g.ClientId,
                    Name = g.Name,
                    Description = g.Description,
                    Context = g.Context,
                    Slug = g.Slug,
                    CreatedAt = g.CreatedAt,
                    CompanyCount = g.TrackedCompanyGroups.Count()
                })
                .ToListAsync();
        }

        public async Task<CompanyGroupDto?> GetDefaultGroupForClientAsync(int clientId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var defaultSlug = $"default-{clientId}";

            return await db.CompanyGroups
                .Where(g => g.ClientId == clientId && g.Slug == defaultSlug)
                .Select(g => new CompanyGroupDto
                {
                    Id = g.Id,
                    ClientId = g.ClientId,
                    Name = g.Name,
                    Description = g.Description,
                    Context = g.Context,
                    Slug = g.Slug,
                    CreatedAt = g.CreatedAt,
                    CompanyCount = g.TrackedCompanyGroups.Count()
                })
                .FirstOrDefaultAsync();
        }

        public async Task<CompanyGroupDto> CreateCompanyGroupAsync(CompanyGroupDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var slug = await SlugHelper.GenerateUniqueSlugAsync(
                dto.Name,
                async (candidate) =>
                    await db.CompanyGroups.AnyAsync(g =>
                        g.Slug == candidate && g.ClientId == dto.ClientId)
            );

            var entity = new CompanyGroup
            {
                ClientId = dto.ClientId,
                Name = dto.Name,
                Description = dto.Description,
                Context = dto.Context,
                Slug = slug,
                CreatedAt = DateTime.UtcNow,
                IsPrivate = true
            };

            db.CompanyGroups.Add(entity);
            await db.SaveChangesAsync();

            return entity.ToDto();
        }

        public async Task UpdateCompanyGroupAsync(CompanyGroupDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var group = await db.CompanyGroups
                .FirstOrDefaultAsync(g => g.Id == dto.Id && g.ClientId == dto.ClientId);

            if (group == null)
                throw new Exception("Group not found.");

            var isDefault = string.Equals(group.Slug, $"default-{group.ClientId}", StringComparison.OrdinalIgnoreCase);

            // ✅ Only regenerate slug for NON-default groups
            if (!isDefault && !string.Equals(group.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
            {
                var newSlug = await SlugHelper.GenerateUniqueSlugAsync(
                    dto.Name,
                    async (candidate) =>
                        await db.CompanyGroups.AnyAsync(g =>
                            g.Slug == candidate && g.ClientId == dto.ClientId && g.Id != dto.Id)
                );

                group.Slug = newSlug;
            }

            group.Name = dto.Name;
            group.Description = dto.Description;
            group.Context = dto.Context;

            await db.SaveChangesAsync();
        }

        public async Task DeleteCompanyGroupAsync(int groupId, int clientId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var group = await db.CompanyGroups
                .Include(g => g.TrackedCompanyGroups)
                .FirstOrDefaultAsync(g => g.Id == groupId && g.ClientId == clientId);

            if (group == null) return;

            // ✅ Prevent deleting the default group
            if (string.Equals(group.Slug, $"default-{group.ClientId}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The default group cannot be deleted.");

            db.TrackedCompanyGroups.RemoveRange(group.TrackedCompanyGroups);
            db.CompanyGroups.Remove(group);
            await db.SaveChangesAsync();
        }

        public async Task AssignCompanyToGroupAsync(int trackedCompanyId, int groupId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var exists = await db.TrackedCompanyGroups
                .AnyAsync(x => x.TrackedCompanyId == trackedCompanyId && x.CompanyGroupId == groupId);

            if (!exists)
            {
                db.TrackedCompanyGroups.Add(new TrackedCompanyGroup
                {
                    TrackedCompanyId = trackedCompanyId,
                    CompanyGroupId = groupId
                });

                await db.SaveChangesAsync();
            }
        }

        public async Task RemoveCompanyFromGroupAsync(int trackedCompanyId, int groupId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var record = await db.TrackedCompanyGroups
                .FirstOrDefaultAsync(x => x.TrackedCompanyId == trackedCompanyId && x.CompanyGroupId == groupId);

            if (record != null)
            {
                db.TrackedCompanyGroups.Remove(record);
                await db.SaveChangesAsync();
            }
        }

        public async Task<List<int>> GetCompanyIdsForGroupAsync(int groupId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            return await db.TrackedCompanyGroups
                .Where(x => x.CompanyGroupId == groupId)
                .Select(x => x.TrackedCompany.CompanyId)
                .Distinct()
                .ToListAsync();
        }

        public async Task UpdateCompaniesForGroupAsync(int groupId, List<int> companyIds)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var group = await db.CompanyGroups
                .Include(g => g.TrackedCompanyGroups)
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
                throw new Exception("Group not found.");

            var allClientTrackedCompanies = await db.TrackedCompanies
                .Where(tc => tc.ClientId == group.ClientId && companyIds.Contains(tc.CompanyId))
                .ToListAsync();

            var trackedCompanyIdsToKeep = allClientTrackedCompanies
                .Select(tc => tc.Id)
                .ToHashSet();

            var currentGroupLinks = await db.TrackedCompanyGroups
                .Where(x => x.CompanyGroupId == groupId)
                .ToListAsync();

            db.TrackedCompanyGroups.RemoveRange(currentGroupLinks);

            foreach (var trackedCompanyId in trackedCompanyIdsToKeep)
            {
                db.TrackedCompanyGroups.Add(new TrackedCompanyGroup
                {
                    CompanyGroupId = groupId,
                    TrackedCompanyId = trackedCompanyId
                });
            }

            await db.SaveChangesAsync();
        }

        public async Task<List<CompanyGroupDto>> GetPublicCompanyGroupsAsync(int clientId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            return await db.CompanyGroups
            .AsNoTracking()
            .Where(g => g.ClientId == clientId && !g.IsPrivate)

            // ✅ only groups where at least N distinct companies have strategic summaries
            .Where(g =>
                db.StrategicSummaries
                  .Where(s => s.CompanyGroupId == g.Id )
                  .Select(s => s.CompanyId)
                  .Distinct()
                  .Count() >= 2
            )

            .OrderBy(g => g.Name)
            .Select(g => new CompanyGroupDto
            {
                Id = g.Id,
                ClientId = g.ClientId,
                Name = g.Name,
                Description = g.Description,
                Context = g.Context,
                Slug = g.Slug,
                CreatedAt = g.CreatedAt,
                CompanyCount = g.TrackedCompanyGroups.Count()
            })
            .ToListAsync();
        }

    }
}
