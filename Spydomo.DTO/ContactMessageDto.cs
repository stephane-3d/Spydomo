using System.ComponentModel.DataAnnotations;

namespace Spydomo.DTO
{
    public class ContactMessageDto
    {
        [Required]
        public string Name { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; }

        [Required]
        public string Subject { get; set; }

        [Required]
        public string Message { get; set; }

        public int UserId { get; set; } // Fetched server-side
        public int? ClientId { get; set; } // Fetched server-side
    }
}
