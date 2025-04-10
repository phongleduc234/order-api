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
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        OrderDbContext context,
        IBus bus,
        IHttpClientFactory clientFactory,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _bus = bus;
        _clientFactory = clientFactory;
        _logger = logger;

        // Cấu hình Retry Policy với Polly
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (response, delay, retryCount, context) =>
                {
                    _logger.LogWarning($"Retry {retryCount} for payment processing. Delay: {delay}");
                });
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
            // Lưu order vào database
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Gọi PaymentService để xử lý thanh toán (có retry)
            var client = _clientFactory.CreateClient("PaymentService");
            var paymentRequest = new
            {
                OrderId = order.Id,
                Amount = order.Amount,
                CorrelationId = Guid.NewGuid()
            };

            // Sử dụng retry policy
            var response = await _retryPolicy.ExecuteAsync(() =>
                client.PostAsJsonAsync("/api/payments/process", paymentRequest));

            if (!response.IsSuccessStatusCode)
            {
                await transaction.RollbackAsync();
                await _bus.Publish(new OrderCompensated(Guid.NewGuid(), order.Id));
                return BadRequest("Payment processing failed after retries");
            }

            // Publish sự kiện OrderCreated
            await _bus.Publish(new OrderCreated(Guid.NewGuid(), order.Id));
            await transaction.CommitAsync();

            return Ok(order);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating order");
            await _bus.Publish(new OrderCompensated(Guid.NewGuid(), order.Id));
            return StatusCode(500, "Internal server error");
        }
    }
}