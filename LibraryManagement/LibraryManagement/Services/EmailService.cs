// Services/EmailService.cs
using System.Net;
using System.Net.Mail;

namespace LibraryManagement.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var smtpHost = _config["EmailSettings:SmtpHost"]!;
            var smtpPort = int.Parse(_config["EmailSettings:SmtpPort"]!);
            var fromEmail = _config["EmailSettings:FromEmail"]!;
            var fromName = _config["EmailSettings:FromName"]!;
            var password = _config["EmailSettings:Password"]!;

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(fromEmail, password)
            };

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent to {Email} with subject: {Subject}", toEmail, subject);
        }
    }
}
