using Spydomo.DTO;

namespace Spydomo.Models.Extensions
{
    public static class TrackedCompanyExtensions
    {
        public static TrackedCompanyDto ToDto(this TrackedCompany tc)
        {
            return new TrackedCompanyDto
            {
                Id = tc.Id,
                Name = tc.Name,
                Notes = tc.Notes,
                DateCreated = tc.DateCreated,
                CompanyId = tc.Company?.Id ?? 0,
                CompanyName = tc.Company?.Name,
                CompanyUrl = tc.Company?.Url,
                Status = tc.Company?.Status
            };
        }
    }

}
