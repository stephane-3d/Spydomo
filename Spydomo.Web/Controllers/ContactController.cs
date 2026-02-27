using Microsoft.AspNetCore.Mvc;
using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Web.Controllers
{
    [ApiController]
    [Route("api/contact")]
    public class ContactController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly ILogger<ContactController> _logger;
        private readonly IConfiguration _config;

        public ContactController(IEmailService emailService, ILogger<ContactController> logger, IConfiguration config)
        {
            _emailService = emailService;
            _logger = logger;
            _config = config;
        }

        [HttpPost]
        public async Task<IActionResult> SendContactMessage(ContactFormModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest("Invalid form submission.");

            var emailTo = _config["App:NotificationEmailTo"]!;

            var body = $"""
                New Contact Us message:

                Name: {model.Name}
                Email: {model.Email}
                Subject: {model.Subject}
                Message:
                {model.Message}

                Client ID: {model.ClientId}
                User ID: {model.UserId}
            """;

            try
            {
                await _emailService.SendEmailAsync(
                    to: emailTo,
                    subject: model.Subject,
                    body: body,
                    replyTo: model.Email,
                    replyToDisplayName: model.Name);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send contact message");
                return StatusCode(500, $"Failed to send message - {ex.Message}");
            }
        }
    }

}
