namespace Spydomo.DTO
{
    public class WhoAmIResponse
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int? ClientId { get; set; }
        public string? ClientName { get; set; }
    }
}
