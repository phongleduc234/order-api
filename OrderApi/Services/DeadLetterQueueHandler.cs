using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Models;
using SharedContracts.Events;
using System.Text.Json;

namespace OrderApi.Services
{
    public interface IDeadLetterQueueHandler
    {
        Task HandleFailedMessage(string message, string error, string source);
        Task ProcessDeadLetterMessages();
    }

    public class DeadLetterQueueHandler : IDeadLetterQueueHandler
    {
        private readonly ILogger<DeadLetterQueueHandler> _logger;
        private readonly OrderDbContext _dbContext;
        private readonly IBus _bus;
        private readonly IAlertService _alertService;

        public DeadLetterQueueHandler(
            ILogger<DeadLetterQueueHandler> logger,
            OrderDbContext dbContext,
            IBus bus,
            IAlertService alertService)
        {
            _logger = logger;
            _dbContext = dbContext;
            _bus = bus;
            _alertService = alertService;
        }

        public async Task HandleFailedMessage(string message, string error, string source)
        {
            try
            {
                var deadLetterMessage = new DeadLetterMessage
                {
                    MessageContent = message,
                    Error = error,
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    RetryCount = 0,
                    Status = DeadLetterStatus.Pending
                };

                _dbContext.DeadLetterMessages.Add(deadLetterMessage);
                await _dbContext.SaveChangesAsync();

                _logger.LogWarning($"Message moved to dead letter queue. Source: {source}, Error: {error}");
                
                // Send alert for failed message
                await _alertService.SendWebhookAlertAsync(
                    "Message Processing Failed",
                    $"Source: {source}\nError: {error}\nMessage: {message}",
                    ""
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling dead letter message");
                throw;
            }
        }

        public async Task ProcessDeadLetterMessages()
        {
            try
            {
                var pendingMessages = await _dbContext.DeadLetterMessages
                    .Where(m => m.Status == DeadLetterStatus.Pending && m.RetryCount < 3)
                    .ToListAsync();

                foreach (var message in pendingMessages)
                {
                    try
                    {
                        // Deserialize the message content based on the source
                        switch (message.Source)
                        {
                            case "OrderCreated":
                                var orderCreated = JsonSerializer.Deserialize<OrderCreated>(message.MessageContent);
                                if (orderCreated != null)
                                {
                                    await _bus.Publish(orderCreated);
                                    _logger.LogInformation($"Republished OrderCreated event for order {orderCreated.OrderId}");
                                }
                                break;

                            case "OrderFulfilled":
                                var orderFulfilled = JsonSerializer.Deserialize<OrderFulfilled>(message.MessageContent);
                                if (orderFulfilled != null)
                                {
                                    await _bus.Publish(orderFulfilled);
                                    _logger.LogInformation($"Republished OrderFulfilled event for order {orderFulfilled.OrderId}");
                                }
                                break;

                            case "CompensateOrder":
                                var compensateOrder = JsonSerializer.Deserialize<CompensateOrder>(message.MessageContent);
                                if (compensateOrder != null)
                                {
                                    await _bus.Publish(compensateOrder);
                                    _logger.LogInformation($"Republished CompensateOrder event for order {compensateOrder.OrderId}");
                                }
                                break;

                            default:
                                _logger.LogWarning($"Unknown message source: {message.Source}");
                                break;
                        }

                        message.RetryCount++;
                        message.LastRetryAt = DateTime.UtcNow;

                        if (message.RetryCount >= 3)
                        {
                            message.Status = DeadLetterStatus.Failed;
                            _logger.LogError($"Message permanently failed after {message.RetryCount} retries. Source: {message.Source}");
                            
                            // Send alert for permanent failure
                            await _alertService.SendWebhookAlertAsync(
                                "Message Processing Permanently Failed",
                                $"Source: {message.Source}\nError: {message.Error}\nMessage: {message.MessageContent}",
                                message.Id.ToString()
                            );
                        }
                        else
                        {
                            message.Status = DeadLetterStatus.Processed;
                            _logger.LogInformation($"Message processed successfully. Source: {message.Source}");
                        }

                        await _dbContext.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing dead letter message {message.Id}");
                        message.RetryCount++;
                        message.LastRetryAt = DateTime.UtcNow;

                        if (message.RetryCount >= 3)
                        {
                            message.Status = DeadLetterStatus.Failed;
                            await _alertService.SendWebhookAlertAsync(
                                "Message Processing Error",
                                $"Error processing message: {ex.Message}\nSource: {message.Source}",
                                message.Id.ToString()
                            );
                        }

                        await _dbContext.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing dead letter messages");
                throw;
            }
        }
    }
} 