namespace Spydomo.Models
{
    public class FilteredUrl
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        public string PostUrl { get; set; } = string.Empty;

        public int SourceTypeId { get; set; }
        public DataSourceType SourceType { get; set; } = null!;

        public string Reason { get; set; } = "Unknown";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }


}
