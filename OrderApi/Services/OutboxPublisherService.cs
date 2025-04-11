using MassTransit;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OrderService.Data;

namespace OrderApi.Services
{
    /// <summary>
    /// Outbox Service (Xử lý Reliable Messaging)
    /// </summary>
    public class OutboxPublisherService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OutboxPublisherService> _logger;
        private readonly IAlertService _alertService;
        private readonly int _maxRetryCount = 5;
        private readonly int _alertThreshold = 3;

        public OutboxPublisherService(
            IServiceProvider serviceProvider,
            ILogger<OutboxPublisherService> logger,
            IAlertService alertService,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _alertService = alertService;
            _maxRetryCount = configuration.GetValue("Outbox:MaxRetryCount", 5);
            _alertThreshold = configuration.GetValue("Alerting:AlertThreshold", 3);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Outbox Publisher Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboxMessages(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing outbox messages");
                }

                await Task.Delay(5000, stoppingToken); // Kiểm tra mỗi 5 giây
            }

            _logger.LogInformation("Outbox Publisher Service stopped");
        }

        private async Task ProcessOutboxMessages(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();

            // Lock để ngăn multiple instances xử lý cùng lúc trong môi trường multi-instance
            var messages = await dbContext.OutboxMessages
                .Where(m => !m.Processed && m.RetryCount < _maxRetryCount)
                .OrderBy(m => m.CreatedAt)
                .Take(100)
                .ToListAsync(stoppingToken);

            if (messages.Any())
            {
                _logger.LogInformation($"Found {messages.Count} messages to process");
            }

            foreach (var message in messages)
            {
                try
                {
                    var eventType = Type.GetType(message.EventType);
                    if (eventType == null)
                    {
                        _logger.LogWarning($"Could not resolve event type: {message.EventType}");
                        message.Processed = true; // Đánh dấu là đã xử lý để tránh retry vĩnh viễn
                        continue;
                    }

                    var eventData = JsonConvert.DeserializeObject(message.EventData, eventType);

                    _logger.LogInformation($"Publishing message {message.Id} of type {eventType.Name}");
                    await bus.Publish(eventData, stoppingToken);

                    message.Processed = true;
                    message.ProcessedAt = DateTime.UtcNow;
                    _logger.LogInformation($"Message {message.Id} processed successfully");
                }
                catch (Exception ex)
                {
                    message.RetryCount++;
                    _logger.LogError(ex, $"Failed to publish message {message.Id}. Retry count: {message.RetryCount}");

                    // Nếu đã đạt số lần retry tối đa, đánh dấu là đã xử lý và log lỗi
                    if (message.RetryCount >= _maxRetryCount)
                    {
                        _logger.LogError($"Message {message.Id} exceeded maximum retry count. Marking as processed.");
                        message.Processed = true;
                        message.ProcessedAt = DateTime.UtcNow;
                    }
                    // Gửi cảnh báo khi vượt ngưỡng thử lại
                    if (message.RetryCount >= _alertThreshold)
                    {
                        await _alertService.SendWebhookAlertAsync(
                            message.EventType,
                            ex.Message,
                            message.Id.ToString()
                        );

                        // Với lỗi nghiêm trọng, gửi email
                        if (message.RetryCount >= _maxRetryCount - 1)
                        {
                            await _alertService.SendEmailAlertAsync(
                                message.EventType,
                                ex.Message,
                                message.Id.ToString()
                            );
                        }
                    }
                }

                // Lưu lại trạng thái sau mỗi message để tránh mất tiến độ nếu crash
                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
