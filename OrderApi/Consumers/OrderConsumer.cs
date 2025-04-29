using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using SharedContracts.Events;

namespace OrderApi.Consumers
{
    /// <summary>
    /// Handles compensation for orders when part of the saga fails.
    /// Responsible for canceling orders and cleaning up order data.
    /// </summary>
    public class OrderConsumer : IConsumer<CompensateOrder>
    {
        private readonly OrderDbContext _context;
        private readonly ILogger<OrderConsumer> _logger;
        private readonly IBus _bus;

        public OrderConsumer(
            OrderDbContext context,
            ILogger<OrderConsumer> logger,
            IBus bus)
        {
            _context = context;
            _logger = logger;
            _bus = bus;
        }

        /// <summary>
        /// Handles compensation requests for orders.
        /// Deletes the order from the database and publishes OrderCompensated event.
        /// </summary>
        public async Task Consume(ConsumeContext<CompensateOrder> context)
        {
            _logger.LogInformation($"Compensating order {context.Message.OrderId}");

            // Find the order to compensate
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);

            if (order != null)
            {
                // Delete the order
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Order {context.Message.OrderId} successfully compensated");
            }
            else
            {
                _logger.LogWarning($"Order {context.Message.OrderId} not found for compensation");
            }

            // Always publish OrderCompensated event to complete the saga
            // This ensures the saga progresses even if the order doesn't exist
            await _bus.Publish(new OrderCompensated(
                context.Message.CorrelationId,
                context.Message.OrderId));
        }
    }
}