using MassTransit;
using Microsoft.AspNetCore.Mvc;
using OrderApi.Extensions;
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

            // Gọi PaymentService để xử lý thanh toán
            var client = _clientFactory.CreateClient("PaymentService");
            var paymentRequest = new
            {
                OrderId = order.Id,
                Amount = order.Amount,
                CorrelationId = Guid.NewGuid()
            };

            var response = await _retryPolicy.ExecuteAsync(() =>
                client.PostAsJsonAsync("/api/payments/process", paymentRequest));

            if (!response.IsSuccessStatusCode)
            {
                // Đảm bảo transaction được commit trước khi thêm sự kiện bồi hoàn
                await transaction.CommitAsync();

                // Tạo transaction mới để lưu sự kiện bồi thường
                using var compensateTransaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var compensateEvent = new OrderCompensated(Guid.NewGuid(), order.Id);
                    await _context.SaveEventToOutboxAsync(compensateEvent);
                    await compensateTransaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving compensate event to outbox");
                    await compensateTransaction.RollbackAsync();
                }

                return BadRequest("Payment processing failed after retries");
            }

            // Lưu sự kiện OrderCreated vào Outbox
            var orderCreatedEvent = new OrderCreated(Guid.NewGuid(), order.Id);
            await _context.SaveEventToOutboxAsync(orderCreatedEvent);

            // Commit transaction
            await transaction.CommitAsync();
            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");

            try
            {
                await transaction.RollbackAsync();

                // Lưu sự kiện bồi thường trong transaction mới
                using var compensateTransaction = await _context.Database.BeginTransactionAsync();
                var compensateEvent = new OrderCompensated(Guid.NewGuid(), order.Id);
                await _context.SaveEventToOutboxAsync(compensateEvent);
                await compensateTransaction.CommitAsync();
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to save compensate event to outbox");
            }

            return StatusCode(500, "Internal server error");
        }
    }
}