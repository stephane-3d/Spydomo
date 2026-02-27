namespace Spydomo.DTO
{
    public class UserDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public int VisitsCount { get; set; }
        public DateTime DateCreated { get; set; }
        public int ClientId { get; set; }

        public string InvitationStatus { get; set; }
    }
}
