// OrderApi/Services/AlertService.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrderApi.Services
{
    public class AlertService : IAlertService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<AlertService> _logger;
        private readonly string _webhookUrl;
        private readonly string _emailApiUrl;
        private readonly string _apiKey;
        private readonly string _adminEmail;
        private readonly string _adminCcEmail;
        private readonly string _adminBccEmail;
        public AlertService(
            IHttpClientFactory clientFactory,
            ILogger<AlertService> logger,
            IConfiguration config)
        {
            _clientFactory = clientFactory;
            _logger = logger;
            _webhookUrl = config["Telegram:WebhookBaseUrl"];
            _emailApiUrl = config["EmailApi:ApiUrl"];
            _adminEmail = config["SmtpMail:AdminEmail"];
            _adminCcEmail = config["SmtpMail:AdminCcEmail"];
            _adminBccEmail = config["SmtpMail:AdminBccEmail"];
            _apiKey = config["EmailApi:ApiKey"];
        }

        public async Task SendWebhookAlertAsync(string messageType, string error, string messageId)
        {
            try
            {
                if (string.IsNullOrEmpty(_webhookUrl))
                {
                    _logger.LogWarning("Webhook URL is not configured. Alert not sent.");
                    return;
                }

                var client = _clientFactory.CreateClient();

                // Construct the message for Telegram
                var message = $"*ALERT: {messageType}*\n" +
                             $"Message ID: {messageId}\n" +
                             $"Error: {error}\n" +
                             $"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";

                // URL encode the message
                var encodedMessage = Uri.EscapeDataString(message);
                var fullUrl = $"{_webhookUrl}{encodedMessage}";

                var response = await client.GetAsync(fullUrl);

                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Webhook alert failed with status {StatusCode}: {Response}",
                        response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send webhook alert");
            }
        }

        public async Task SendEmailAlertAsync(string messageType, string error, string messageId)
        {
            try
            {
                if (string.IsNullOrEmpty(_emailApiUrl))
                {
                    _logger.LogWarning("Email API URL is not configured. Alert not sent.");
                    return;
                }

                if (string.IsNullOrEmpty(_apiKey))
                {
                    _logger.LogWarning("API Key is not configured. Email alert not sent.");
                    return;
                }

                var client = _clientFactory.CreateClient();

                // Configure request headers
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("apiKey", _apiKey);

                // Prepare HTML body for better formatting
                var htmlBody = $@"
                <h2>Message Processing Error: {messageType}</h2>
                <p><strong>Message ID:</strong> {messageId}</p>
                <p><strong>Error:</strong> {error}</p>
                <p><strong>Timestamp:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</p>
                <hr>
                <p><em>This is an automated alert from the Order API system.</em></p>";

                // Create email request object
                var emailRequest = new
                {
                    to = _adminEmail,
                    subject = $"ALERT: Message Processing Error - {messageType}",
                    body = htmlBody,
                    isHtml = true,
                    cc = new[] { _adminCcEmail?.Split(',') },
                    bcc = new[] { _adminBccEmail?.Split(',') }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(emailRequest),
                    Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync(_emailApiUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Email alert failed with status {StatusCode}: {Response}",
                        response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email alert");
            }
        }

        public async Task SendCombinedAlertAsync(string messageType, string error, string messageId)
        {
            await SendEmailAlertAsync(messageType, error, messageId);
            await SendWebhookAlertAsync(messageType, error, messageId);
        }
    }

    public interface IAlertService
    {
        Task SendWebhookAlertAsync(string messageType, string error, string messageId);
        Task SendEmailAlertAsync(string messageType, string error, string messageId);
        Task SendCombinedAlertAsync(string messageType, string error, string messageId);
    }
}
