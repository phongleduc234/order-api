// OrderApi/Services/AlertService.cs
namespace OrderApi.Services
{
    public class AlertService : IAlertService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<AlertService> _logger;
        private readonly string _webhookUrl;
        private readonly string _emailApiUrl;

        public AlertService(
            IHttpClientFactory clientFactory, 
            ILogger<AlertService> logger,
            IConfiguration config)
        {
            _clientFactory = clientFactory;
            _logger = logger;
            _webhookUrl = config["Alerting:WebhookUrl"];
            _emailApiUrl = config["Alerting:EmailApiUrl"];
        }

        public async Task SendWebhookAlertAsync(string messageType, string error, string messageId)
        {
            try
            {
                var client = _clientFactory.CreateClient();
                await client.PostAsJsonAsync(_webhookUrl, new
                {
                    MessageType = messageType,
                    Error = error,
                    MessageId = messageId,
                    Timestamp = DateTime.UtcNow
                });
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
                var client = _clientFactory.CreateClient();
                await client.PostAsJsonAsync(_emailApiUrl, new
                {
                    To = "ops-team@yourcompany.com",
                    Subject = $"Message Processing Error: {messageType}",
                    Body = $"Message ID: {messageId}\nError: {error}\nTimestamp: {DateTime.UtcNow}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email alert");
            }
        }
    }

    public interface IAlertService
    {
        Task SendWebhookAlertAsync(string messageType, string error, string messageId);
        Task SendEmailAlertAsync(string messageType, string error, string messageId);
    }
}
