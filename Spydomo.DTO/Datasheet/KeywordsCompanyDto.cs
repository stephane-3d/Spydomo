namespace Spydomo.DTO.Datasheet
{
    public sealed class KeywordsCompanyDto
    {
        public int CompanyId { get; set; }
        public string Company { get; set; } = "";
        public string Url { get; set; } = "";
        public List<KeywordRow> Keywords { get; set; } = new();
    }
}
