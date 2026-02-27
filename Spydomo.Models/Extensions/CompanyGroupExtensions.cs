using Spydomo.DTO;

namespace Spydomo.Models.Extensions
{
    public static class CompanyGroupExtensions
    {
        public static CompanyGroupDto ToDto(this CompanyGroup group)
        {
            return new CompanyGroupDto
            {
                Id = group.Id,
                ClientId = group.ClientId,
                Name = group.Name,
                Description = group.Description,
                Context = group.Context,
                Slug = group.Slug,
                CreatedAt = group.CreatedAt,
                CompanyCount = group.TrackedCompanyGroups?.Count ?? 0
            };
        }
    }
}
