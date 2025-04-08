using MassTransit;
using OrderService.Data;

namespace OrderApi.Shared
{
    // OrderConsumer.cs
    public class OrderConsumer : IConsumer<OrderCompensated>
    {
        private readonly OrderDbContext _context;

        public OrderConsumer(OrderDbContext context)
        {
            _context = context;
        }

        public async Task Consume(ConsumeContext<OrderCompensated> context)
        {
            var order = await _context.Orders.FindAsync(context.Message.OrderId);
            if (order != null)
            {
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();
            }
        }
    }
}
