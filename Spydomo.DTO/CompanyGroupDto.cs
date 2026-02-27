namespace Spydomo.DTO
{
    public class CompanyGroupDto
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Context { get; set; }
        public string Slug { get; set; } = "";
        public int CompanyCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
