namespace Spydomo.Infrastructure.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(
            string to,
            string subject,
            string body,
            string? replyTo = null,
            string? replyToDisplayName = null,
            CancellationToken ct = default);
    }
}
