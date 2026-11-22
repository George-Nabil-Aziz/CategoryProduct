using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    /// <summary>Optional SMTP transport. Settings come from configuration only; nothing secret is logged.</summary>
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly EmailOptions _options;

        public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var problem = message.Validate();
            if (problem != null)
                throw new InvalidOperationException("Email message rejected: " + problem);

            using var mail = CreateMailMessage(_options, message);
            using var client = new SmtpClient(_options.Smtp.Host!.Trim(), _options.Smtp.Port)
            {
                EnableSsl = _options.Smtp.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000
            };
            if (!string.IsNullOrWhiteSpace(_options.Smtp.UserName))
                client.Credentials = new NetworkCredential(_options.Smtp.UserName, _options.Smtp.Password ?? string.Empty);
            await client.SendMailAsync(mail, cancellationToken);
        }

        /// <summary>Builds the plain-text message (no network, no credentials).</summary>
        public static MailMessage CreateMailMessage(EmailOptions options, EmailMessage message) =>
            new(options.EffectiveFrom.Replace("\r", "").Replace("\n", ""), message.To, message.Subject, message.Body)
            {
                IsBodyHtml = false,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8
            };
    }
}
