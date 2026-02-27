using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure
{
    public sealed class EmailService : IEmailService
    {
        private readonly EmailClient _client;
        private readonly ILogger<EmailService> _logger;
        private readonly string _from;
        private readonly string? _defaultReplyTo;
        private readonly string? _defaultReplyToName;

        public EmailService(EmailClient client, IConfiguration cfg, ILogger<EmailService> logger)
        {
            _client = client;
            _logger = logger;

            _from = cfg["AcsEmail:From"] ?? throw new InvalidOperationException("Missing config: AcsEmail:From");
            _defaultReplyTo = cfg["AcsEmail:DefaultReplyTo"];              // optional
            _defaultReplyToName = cfg["AcsEmail:DefaultReplyToName"];      // optional
        }

        public async Task SendEmailAsync(
            string to,
            string subject,
            string body,
            string? replyTo = null,
            string? replyToDisplayName = null,
            CancellationToken ct = default)
        {
            try
            {
                var content = new EmailContent(subject) { PlainText = body };
                var recipients = new EmailRecipients(new[] { new EmailAddress(to) });
                var message = new EmailMessage(_from, recipients, content);

                // Prefer explicit reply-to from caller; otherwise use default (if configured)
                var rt = string.IsNullOrWhiteSpace(replyTo) ? _defaultReplyTo : replyTo;
                var rtName = string.IsNullOrWhiteSpace(replyTo) ? _defaultReplyToName : replyToDisplayName;

                if (!string.IsNullOrWhiteSpace(rt))
                    message.ReplyTo.Add(new EmailAddress(rt, rtName));

                var op = await _client.SendAsync(WaitUntil.Completed, message, ct);

                _logger.LogInformation(
                    "ACS Email send completed. OperationId={OperationId}, Status={Status}, To={To}, From={From}, ReplyTo={ReplyTo}",
                    op.Id, op.Value.Status, to, _from, rt);

                if (op.Value.Status != EmailSendStatus.Succeeded)
                    throw new Exception($"ACS Email failed. Status={op.Value.Status}. OperationId={op.Id}");
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "ACS Email request failed. To={To}, Subject={Subject}, ErrorCode={ErrorCode}, Message={Message}",
                    to, subject, ex.ErrorCode, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ACS Email send failed. To={To}, Subject={Subject}", to, subject);
                throw;
            }
        }
    }


}
