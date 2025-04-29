// OrderApi/Controllers/OutboxController.cs
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OrderApi.Data;

[ApiController]
[Route("[controller]")]
public class OutboxController : ControllerBase
{
    private readonly OrderDbContext _context;
    private readonly IBus _bus;
    private readonly ILogger<OutboxController> _logger;

    public OutboxController(
        OrderDbContext context,
        IBus bus,
        ILogger<OutboxController> logger)
    {
        _context = context;
        _bus = bus;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetOutboxMessages(
        bool? processed = null,
        int? minRetryCount = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _context.OutboxMessages.AsQueryable();

        if (processed.HasValue)
        {
            query = query.Where(m => m.Processed == processed.Value);
        }

        if (minRetryCount.HasValue)
        {
            query = query.Where(m => m.RetryCount >= minRetryCount.Value);
        }

        var totalCount = await query.CountAsync();
        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Messages = messages.Select(m => new
            {
                m.Id,
                m.EventType,
                m.EventData,
                m.CreatedAt,
                m.Processed,
                m.RetryCount,
                m.ProcessedAt
            })
        });
    }

    [HttpPost("{id}/retry")]
    public async Task<IActionResult> RetryMessage(Guid id)
    {
        var message = await _context.OutboxMessages.FindAsync(id);

        if (message == null)
            return NotFound();

        message.Processed = false;
        message.RetryCount = 0;

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Message marked for retry" });
    }

    [HttpPost("{id}/process-manually")]
    public async Task<IActionResult> ProcessMessageManually(Guid id)
    {
        var message = await _context.OutboxMessages.FindAsync(id);

        if (message == null)
            return NotFound();

        try
        {
            var eventType = Type.GetType(message.EventType);
            if (eventType == null)
                return BadRequest(new { Error = "Could not resolve event type" });

            var eventData = JsonConvert.DeserializeObject(message.EventData, eventType);

            // Publish manually to the message bus
            await _bus.Publish(eventData);

            // Update message status
            message.Processed = true;
            message.ProcessedAt = DateTime.UtcNow;
            message.RetryCount += 1;
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Message {id} processed manually");

            return Ok(new { Message = "Message processed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process message {id} manually");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMessage(Guid id)
    {
        var message = await _context.OutboxMessages.FindAsync(id);

        if (message == null)
            return NotFound();

        _context.OutboxMessages.Remove(message);
        await _context.SaveChangesAsync();

        _logger.LogWarning($"Message {id} deleted manually");

        return Ok(new { Message = "Message deleted" });
    }
}
