using MassTransit;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OrderService.Data;
using StackExchange.Redis;
using System.Net;

namespace OrderApi.Services
{
    /// <summary>
    /// Outbox Service (Xử lý Reliable Messaging) with Redis distributed locking
    /// </summary>
    public class OutboxPublisherService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OutboxPublisherService> _logger;
        private readonly IAlertService _alertService;
        private readonly ConnectionMultiplexer _redis;
        private readonly int _maxRetryCount = 5;
        private readonly int _alertThreshold = 3;
        private readonly string _instanceId;

        public OutboxPublisherService(
            IServiceProvider serviceProvider,
            ILogger<OutboxPublisherService> logger,
            IAlertService alertService,
            ConnectionMultiplexer redis,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _alertService = alertService;
            _redis = redis;
            _maxRetryCount = configuration.GetValue("Outbox:MaxRetryCount", 5);
            _alertThreshold = configuration.GetValue("Alerting:AlertThreshold", 3);

            // Generate a unique ID for this instance
            _instanceId = $"{Dns.GetHostName()}-{Guid.NewGuid()}";
            _logger.LogInformation($"Outbox Publisher Service initialized with instance ID: {_instanceId}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Outbox Publisher Service started on instance: {_instanceId}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Acquire global lock before processing
                    var lockAcquired = await TryAcquireLockAsync();

                    if (lockAcquired)
                    {
                        try
                        {
                            _logger.LogInformation($"Lock acquired by instance {_instanceId}. Processing messages...");
                            await ProcessOutboxMessages(stoppingToken);
                        }
                        finally
                        {
                            await ReleaseLockAsync();
                            _logger.LogInformation($"Lock released by instance {_instanceId}");
                        }
                    }
                    else
                    {
                        _logger.LogDebug($"Instance {_instanceId} waiting for lock...");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error occurred on instance {_instanceId} while processing outbox messages");
                    await _alertService.SendCombinedAlertAsync(
                        "OutboxPublisherService",
                        $"Error in outbox processing: {ex.Message}",
                        _instanceId);
                }

                await Task.Delay(5000, stoppingToken); // Kiểm tra mỗi 5 giây
            }

            _logger.LogInformation($"Outbox Publisher Service stopped on instance: {_instanceId}");
        }

        private async Task<bool> TryAcquireLockAsync()
        {
            var db = _redis.GetDatabase();
            // Lock key with 30 second expiry to prevent deadlocks
            // (in case an instance crashes without releasing the lock)
            return await db.StringSetAsync(
                "outbox_processing_lock",
                _instanceId,
                TimeSpan.FromSeconds(30),
                When.NotExists);
        }

        private async Task ReleaseLockAsync()
        {
            var db = _redis.GetDatabase();

            // Lua script to ensure we only delete our own lock
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            await db.ScriptEvaluateAsync(
                script,
                new RedisKey[] { "outbox_processing_lock" },
                new RedisValue[] { _instanceId });
        }

        private async Task ProcessOutboxMessages(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();

            // Lấy các message chưa xử lý và chưa vượt quá số lần retry
            var messages = await dbContext.OutboxMessages
                .Where(m => !m.Processed && m.RetryCount < _maxRetryCount)
                .OrderBy(m => m.CreatedAt)
                .Take(100)
                .ToListAsync(stoppingToken);

            if (messages.Any())
            {
                _logger.LogInformation($"Instance {_instanceId} found {messages.Count} messages to process");
            }

            foreach (var message in messages)
            {
                try
                {
                    // Lock message level để đảm bảo không bị xử lý đồng thời bởi instance khác
                    // trong trường hợp instance này bị treo giữa chừng
                    var messageKey = $"outbox_message_{message.Id}";
                    var db = _redis.GetDatabase();
                    var messageLockAcquired = await db.StringSetAsync(messageKey, _instanceId,
                        TimeSpan.FromMinutes(5), When.NotExists);

                    if (!messageLockAcquired)
                    {
                        _logger.LogDebug($"Message {message.Id} is being processed by another instance. Skipping.");
                        continue;
                    }

                    try
                    {
                        var eventType = Type.GetType(message.EventType);
                        if (eventType == null)
                        {
                            _logger.LogWarning($"Could not resolve event type: {message.EventType}");
                            message.Processed = true; // Đánh dấu là đã xử lý để tránh retry vĩnh viễn
                            await dbContext.SaveChangesAsync(stoppingToken);
                            continue;
                        }

                        var eventData = JsonConvert.DeserializeObject(message.EventData, eventType);

                        _logger.LogInformation($"Instance {_instanceId} publishing message {message.Id} of type {eventType.Name}");
                        await bus.Publish(eventData, stoppingToken);

                        message.Processed = true;
                        message.ProcessedAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation($"Message {message.Id} processed successfully by instance {_instanceId}");
                    }
                    catch (Exception ex)
                    {
                        message.RetryCount++;
                        _logger.LogError(ex, $"Instance {_instanceId} failed to publish message {message.Id}. Retry count: {message.RetryCount}");

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
                                await _alertService.SendCombinedAlertAsync(
                                    message.EventType,
                                    ex.Message,
                                    message.Id.ToString()
                                );
                            }
                        }

                        // Lưu lại trạng thái sau mỗi message để tránh mất tiến độ nếu crash
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                    finally
                    {
                        // Xóa message lock sau khi xử lý xong
                        await db.KeyDeleteAsync(messageKey);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unhandled exception processing message {message.Id} by instance {_instanceId}");
                }
            }
        }
    }
}
