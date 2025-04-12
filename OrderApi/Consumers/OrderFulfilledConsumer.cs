using MassTransit;
using OrderService.Data;
using SharedContracts.Events;

namespace OrderApi.Consumers
{
    // OrderConsumer.cs
    public class OrderFulfilledConsumer : IConsumer<OrderFulfilled>
    {
        private readonly OrderDbContext _context;
        private readonly ILogger<OrderFulfilledConsumer> _logger;

        public OrderFulfilledConsumer(OrderDbContext context, ILogger<OrderFulfilledConsumer> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<OrderFulfilled> context)
        {
            var orderId = context.Message.OrderId;
            _logger.LogInformation($"Received OrderFulfilled event for Order {orderId}");

            var order = await _context.Orders.FindAsync(orderId);
            if (order != null)
            {
                order.Status = "Fulfilled";
                await _context.SaveChangesAsync();
            }
            else
            {
                _logger.LogWarning($"Order {orderId} not found when processing OrderFulfilled event");
            }
        }
    }
}
