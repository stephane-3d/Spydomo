using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Utilities;

namespace Spydomo.Infrastructure
{
    public class SlugService : ISlugService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public SlugService(IDbContextFactory<SpydomoContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<string> GenerateUniqueSlugAsync(string input, EntityType type)
        {
            var baseSlug = SlugHelper.GenerateSlug(input);
            var slug = baseSlug;
            int i = 2;

            while (await SlugExistsAsync(slug, type))
            {
                slug = $"{baseSlug}-{i}";
                i++;
            }

            return slug;
        }

        private async Task<bool> SlugExistsAsync(string slug, EntityType type, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return type switch
            {
                EntityType.Company => await db.Companies.AnyAsync(c => c.Slug == slug),
                EntityType.Group => await db.CompanyGroups.AnyAsync(g => g.Slug == slug),
                EntityType.Tag => await db.CanonicalTags.AnyAsync(t => t.Slug == slug),
                EntityType.Theme => await db.CanonicalThemes.AnyAsync(t => t.Slug == slug),
                _ => throw new NotImplementedException($"Slug check not implemented for {type}")
            };
        }
    }

}
