using Spydomo.Utilities;

namespace Spydomo.DTO
{
    public class TrackedCompanyDto
    {
        public int Id { get; set; }

        public string? Name { get; set; }           // Optional override name
        public string? Notes { get; set; }
        public DateTime? DateCreated { get; set; }

        // Embedded Company info
        public int CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyUrl { get; set; }
        public string? Status { get; set; }

        public override bool Equals(object? obj) =>
        obj is TrackedCompanyDto other && CompanyId == other.CompanyId;

        public override int GetHashCode() => CompanyId.GetHashCode();

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(Name) ? Name :
            !string.IsNullOrWhiteSpace(CompanyName) ? CompanyName :
            TryDomain(CompanyUrl) ?? "(Unnamed)";

        private static string? TryDomain(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try { return new Uri(UrlHelper.GetHttpsUrl(url)).Host.Replace("www.", ""); } catch { return null; }
        }
    }

}
