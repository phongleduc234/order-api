// OrderApi/Shared/DeadLetterConsumer.cs
using MassTransit;
using OrderApi.Services;

namespace OrderApi.Consumer
{
    // OrderApi/Shared/DeadLetterConsumer.cs
    public class DeadLetterConsumer : IConsumer<object>
    {
        private readonly ILogger<DeadLetterConsumer> _logger;
        private readonly IAlertService _alertService;

        public DeadLetterConsumer(
            ILogger<DeadLetterConsumer> logger,
            IAlertService alertService)
        {
            _logger = logger;
            _alertService = alertService;
        }

        public async Task Consume(ConsumeContext<object> context)
        {
            // Lấy thông tin lỗi từ header
            var messageType = context.Headers.Get("MessageType", "Unknown");
            var exceptionMessage = context.Headers.Get("Exception-Message", "Unknown error");
            var failureCount = context.Headers.Get<int>("FailureCount", 1);
            var messageId = context.MessageId?.ToString() ?? Guid.NewGuid().ToString();

            // Log thông tin lỗi
            _logger.LogError(
                "Dead Letter received: MessageId: {MessageId}, Type: {MessageType}, Error: {Error}, Failures: {FailureCount}",
                messageId, messageType, exceptionMessage, failureCount);

            // Gửi thông báo qua webhook/email
            await _alertService.SendWebhookAlertAsync(messageType, exceptionMessage, messageId);

            // Với lỗi nghiêm trọng, gửi email
            if (failureCount >= 3)
            {
                await _alertService.SendEmailAlertAsync(messageType, exceptionMessage, messageId);
            }
        }
    }
}
