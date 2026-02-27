using Spydomo.DTO;

namespace Spydomo.Models.Extensions
{
    public static class CompanyExtensions
    {
        public static CompanyDto ToDto(this Company c) => new CompanyDto
        {
            Id = c.Id,
            Name = c.Name,
            Url = c.Url,
            Slug = c.Slug,
            DateCreated = c.DateCreated,
            Status = c.Status,
            IsActive = c.IsActive,
            RetryCount = c.RetryCount,
            LastRedditLookup = c.LastRedditLookup,
            LastLinkedinLookup = c.LastLinkedinLookup,
            LastFacebookLookup = c.LastFacebookLookup,
            LastFacebookReviewsLookup = c.LastFacebookReviewsLookup,
            HasFacebookReviews = c.HasFacebookReviews
        };
    }

}
