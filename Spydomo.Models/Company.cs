namespace Spydomo.Models;

public partial class Company
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Slug { get; set; } = null!;
    public DateTime? DateCreated { get; set; }
    public bool? IsActive { get; set; }
    public string? Status { get; set; }
    public int? RetryCount { get; set; }
    public DateTime? LastRedditLookup { get; set; }
    public DateTime? LastLinkedinLookup { get; set; }
    public DateTime? LastFacebookLookup { get; set; }
    public DateTime? LastFacebookReviewsLookup { get; set; }
    public DateTime? LastCompanyDataUpdate { get; set; }
    public bool? HasFacebookReviews { get; set; }


    // NEW: categorization
    public int? PrimaryCategoryId { get; set; }
    public decimal? CategoryConfidence { get; set; }          // 0..1
    public string? CategoryReason { get; set; }               // short reason from GPT
    public string? CategoryEvidenceJson { get; set; }         // JSON array of evidence strings

    public string? SelfTitle { get; set; }         // e.g., nvarchar(200)
    public string? SelfDescription { get; set; }   // e.g., nvarchar(500)
    public string? SelfPositioning { get; set; }   // e.g., nvarchar(200)

    // Navs
    public virtual CompanyCategory? PrimaryCategory { get; set; }
    public virtual ICollection<DataSource> DataSources { get; set; } = new List<DataSource>();
    public virtual ICollection<SummarizedInfo> SummarizedInfos { get; set; } = new List<SummarizedInfo>();
    public virtual ICollection<TrackedCompany> TrackedCompanies { get; set; } = new List<TrackedCompany>();
    public virtual ICollection<RawContent> UserFeedbacks { get; set; } = new List<RawContent>();

    // NEW: multi-select facets
    public virtual ICollection<CompanyTargetSegment> CompanyTargetSegments { get; set; } = new List<CompanyTargetSegment>();
    public virtual ICollection<CompanyUserPersona> CompanyUserPersonas { get; set; } = new List<CompanyUserPersona>();

    public virtual ICollection<CompanyRelation> CompanyRelations { get; set; } = new List<CompanyRelation>();
    public virtual ICollection<CompanyRelation> RelatedToCompanies { get; set; } = new List<CompanyRelation>();
    public virtual ICollection<CompanyRelationRun> CompanyRelationRuns { get; set; } = new List<CompanyRelationRun>();
}

