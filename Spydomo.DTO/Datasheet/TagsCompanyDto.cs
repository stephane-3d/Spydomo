namespace Spydomo.DTO.Datasheet
{
    public sealed class TagsCompanyDto
    {
        public int CompanyId { get; set; }
        public string Company { get; set; } = "";
        public string Url { get; set; } = "";
        public List<TagRow> Tags { get; set; } = new();
    }
}
