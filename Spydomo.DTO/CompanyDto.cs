namespace Spydomo.DTO
{
    public class CompanyDto
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Url { get; set; }

        public string Slug { get; set; } = string.Empty;

        public DateTime? DateCreated { get; set; }

        public string? Status { get; set; }

        public bool? IsActive { get; set; }

        public int? RetryCount { get; set; }

        public DateTime? LastRedditLookup { get; set; }
        public DateTime? LastLinkedinLookup { get; set; }
        public DateTime? LastFacebookLookup { get; set; }
        public DateTime? LastFacebookReviewsLookup { get; set; }

        public string? SelfTitle { get; set; }
        public string? SelfDescription { get; set; }
        public string? SelfPositioning { get; set; }

        public bool? HasFacebookReviews { get; set; }
    }

}
