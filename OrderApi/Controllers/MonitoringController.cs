// OrderApi/Controllers/MonitoringController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OrderApi.Controllers
{
    [ApiController]
    [Route("api/monitoring")]
    public class MonitoringController : ControllerBase
    {
        private readonly OrderDbContext _context;
        
        public MonitoringController(OrderDbContext context)
        {
            _context = context;
        }
        
        // Get statistics about outbox processing
        [HttpGet("outbox/stats")]
        public async Task<IActionResult> GetOutboxStats()
        {
            var totalMessages = await _context.OutboxMessages.CountAsync();
            var processedMessages = await _context.OutboxMessages.CountAsync(m => m.Processed);
            var pendingMessages = await _context.OutboxMessages.CountAsync(m => !m.Processed);
            var failedMessages = await _context.OutboxMessages.CountAsync(m => !m.Processed && m.RetryCount >= 3);
            var oldMessages = await _context.OutboxMessages.CountAsync(m => !m.Processed && m.CreatedAt < DateTime.UtcNow.AddHours(-1));
            
            return Ok(new
            {
                TotalMessages = totalMessages,
                ProcessedMessages = processedMessages,
                PendingMessages = pendingMessages,
                FailedMessages = failedMessages,
                OldMessages = oldMessages,
                ProcessingRate = totalMessages > 0 ? (double)processedMessages / totalMessages * 100 : 0
            });
        }
        
        // Get health overview of the system
        [HttpGet("health-overview")]
        public async Task<IActionResult> GetHealthOverview()
        {
            var outboxHealth = await GetOutboxHealthAsync();
            var ordersSummary = await GetOrdersSummaryAsync();
            
            return Ok(new
            {
                Timestamp = DateTime.UtcNow,
                OutboxHealth = outboxHealth,
                OrdersSummary = ordersSummary,
                SystemStatus = DetermineSystemStatus(outboxHealth)
            });
        }
        
        private async Task<object> GetOutboxHealthAsync()
        {
            var failedCount = await _context.OutboxMessages
                .Where(m => !m.Processed && m.RetryCount >= 3)
                .CountAsync();
                
            var oldUnprocessedCount = await _context.OutboxMessages
                .Where(m => !m.Processed && m.CreatedAt < DateTime.UtcNow.AddHours(-1))
                .CountAsync();
                
            return new
            {
                FailedMessages = failedCount,
                OldMessages = oldUnprocessedCount,
                Status = failedCount > 10 || oldUnprocessedCount > 20 ? "Degraded" : "Healthy"
            };
        }
        
        private async Task<object> GetOrdersSummaryAsync()
        {
            var totalOrders = await _context.Orders.CountAsync();
            var todaysOrders = await _context.Orders.CountAsync(o => o.CreatedAt.Date == DateTime.UtcNow.Date);
            
            return new
            {
                TotalOrders = totalOrders,
                TodaysOrders = todaysOrders
            };
        }
        
        private string DetermineSystemStatus(object outboxHealth)
        {
            // Access the dynamic object properties
            var health = outboxHealth as dynamic;
            if (health?.Status == "Degraded")
            {
                return "Warning";
            }
            
            return "Healthy";
        }
    }
}
