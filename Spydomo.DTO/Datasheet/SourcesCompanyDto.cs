namespace Spydomo.DTO.Datasheet
{
    public sealed class SourcesCompanyDto
    {
        public int CompanyId { get; set; }
        public string Company { get; set; } = "";
        public string Url { get; set; } = "";
        public List<SourceCountDto> CompanyGenerated { get; set; } = new();
        public List<SourceCountDto> UserGenerated { get; set; } = new();
        public int Total => (CompanyGenerated?.Sum(x => x.Count) ?? 0) + (UserGenerated?.Sum(x => x.Count) ?? 0);
    }
}
