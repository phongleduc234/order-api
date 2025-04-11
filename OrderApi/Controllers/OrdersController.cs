using Microsoft.AspNetCore.Mvc;
using MassTransit;
using OrderService.Data;
using OrderApi.Extensions;
using OrderApi.Shared;
using SharedContracts.Events;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderDbContext _context;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        OrderDbContext context,
        IBus bus,
        IHttpClientFactory clientFactory,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _logger = logger;
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

            // Chỉ publish event, không xử lý payment trực tiếp (Saga sẽ điều phối)
            var orderCreatedEvent = new OrderCreated(Guid.NewGuid(), order.Id);

            // Lưu event vào Outbox
            await _context.SaveEventToOutboxAsync(orderCreatedEvent);

            await transaction.CommitAsync();
            return Ok(new
            {
                OrderId = order.Id,
                Message = "Order created and saga started"
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating order");

            try
            {
                // Nếu có lỗi, lưu event bồi thường trong transaction mới
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

    // Endpoint để nhận kết quả từ saga (optional)
    [HttpPost("update-status")]
    public async Task<IActionResult> UpdateOrderStatus([FromBody] OrderStatusUpdateRequest request)
    {
        var order = await _context.Orders.FindAsync(request.OrderId);

        if (order == null)
            return NotFound();

        order.Status = request.Status;
        await _context.SaveChangesAsync();

        return Ok(new { Message = $"Order status updated to {request.Status}" });
    }
}

public class OrderStatusUpdateRequest
{
    public Guid OrderId { get; set; }
    public string Status { get; set; }
}