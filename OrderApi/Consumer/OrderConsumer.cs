using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using SharedContracts.Events;

namespace OrderApi.Consumer
{
    // OrderConsumer.cs
    public class OrderConsumer : IConsumer<OrderCompensated>
    {
        private readonly OrderDbContext _context;
        private readonly ILogger<OrderConsumer> _logger;

        public OrderConsumer(OrderDbContext context, ILogger<OrderConsumer> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<OrderCompensated> context)
        {
            _logger.LogInformation($"Compensating order {context.Message.OrderId}");

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);

            if (order != null)
            {
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();
            }
        }
    }
}
