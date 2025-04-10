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

        public OutboxPublisherService(
            IServiceProvider serviceProvider,
            ILogger<OutboxPublisherService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var bus = scope.ServiceProvider.GetRequiredService<IBus>();

                var messages = await dbContext.OutboxMessages
                    .Where(m => !m.Processed)
                    .OrderBy(m => m.CreatedAt)
                    .Take(100)
                    .ToListAsync(stoppingToken);

                foreach (var message in messages)
                {
                    try
                    {
                        var eventType = Type.GetType(message.EventType);
                        var eventData = JsonConvert.DeserializeObject(message.EventData, eventType);

                        await bus.Publish(eventData, stoppingToken);
                        message.Processed = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to publish message {message.Id}");
                    }
                }

                await dbContext.SaveChangesAsync(stoppingToken);
                await Task.Delay(5000, stoppingToken); // Kiểm tra mỗi 5 giây
            }
        }
    }
}
