using System.ComponentModel.DataAnnotations;

namespace Spydomo.DTO
{
    public class ContactFormModel
    {
        [Required(ErrorMessage = "Please enter your name.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a subject.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a message.")]
        public string Message { get; set; } = string.Empty;

        public int ClientId { get; set; }
        public int UserId { get; set; }
    }
}
