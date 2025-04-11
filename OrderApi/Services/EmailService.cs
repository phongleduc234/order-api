// OrderApi/Services/EmailService.cs
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace OrderApi.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpMail");
                var host = smtpSettings["Host"];
                var port = int.Parse(smtpSettings["Port"]);
                var user = smtpSettings["User"];
                var password = smtpSettings["Password"];

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(user, password)
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(user),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml
                };

                message.To.Add(to);

                await client.SendMailAsync(message);
                _logger.LogInformation($"Email sent successfully to {to}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email to {to}");
                return false;
            }
        }
    }
}
