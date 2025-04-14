// OrderApi/Services/EmailService.cs
using System.Net;
using System.Net.Mail;

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
                var user = smtpSettings["UserName"];
                var password = smtpSettings["Password"];
                var fromName = smtpSettings["FromName"] ?? "DevOps";
                //var fromEmail = smtpSettings["FromEmail"] ?? "no-reply@cuder.xyz";
                //var senderEmail = smtpSettings["SenderEmail"];
                Console.WriteLine($"Host : {host}");
                Console.WriteLine($"Port : {port}");
                Console.WriteLine($"User : {user}");
                Console.WriteLine($"Password : {password}");
                Console.WriteLine($"FromName : {fromName}");
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = false,
                    Credentials = new NetworkCredential(user, password)
                };

                using var message = new MailMessage
                {
                    // From with display name
                    From = new MailAddress(user, fromName),

                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml
                };

                message.To.Add(to);

                // (Optional) Thêm Sender nếu muốn chỉ định rõ người gửi thực sự
                //if (!string.IsNullOrWhiteSpace(senderEmail))
                //{
                //    message.Sender = new MailAddress(senderEmail);
                //}

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
