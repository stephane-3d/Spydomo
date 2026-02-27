namespace Spydomo.DTO.Datasheet
{
    public sealed class ThemesCompanyDto
    {
        public int CompanyId { get; set; }
        public string Company { get; set; } = "";
        public string Url { get; set; } = "";
        public List<ThemeRow> Themes { get; set; } = new();
    }
}
