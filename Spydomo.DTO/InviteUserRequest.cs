namespace Spydomo.DTO
{
    public class InviteUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "user"; // default to user
    }

}
