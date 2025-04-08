using MassTransit;
using Microsoft.AspNetCore.Mvc;
using OrderApi.Shared;
using OrderService.Data;
using Polly;
using Polly.Retry;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderDbContext _context;
    private readonly IBus _bus;
    private readonly IHttpClientFactory _clientFactory;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public OrdersController(
        OrderDbContext context,
        IBus bus,
        IHttpClientFactory clientFactory)
    {
        _context = context;
        _bus = bus;
        _clientFactory = clientFactory;

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    [HttpGet]
    public IActionResult GetOrders()
    {
        var orders = _context.Orders.ToList();
        return Ok(orders);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] Order order)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Save order
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Publish event via Outbox
            var @event = new OrderCreated(Guid.NewGuid(), order.Id);
            await _bus.Publish(@event);

            await transaction.CommitAsync();
            return Ok(order);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            await _bus.Publish(new OrderCompensated(Guid.NewGuid(), order.Id));
            return StatusCode(500, ex.Message);
        }
    }
}