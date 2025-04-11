// OrderApi/Health/OutboxHealthCheck.cs
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;

namespace OrderApi.Shared
{
    public class OutboxHealthCheck : IHealthCheck
    {
        private readonly IServiceProvider _serviceProvider;
        
        public OutboxHealthCheck(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, 
            CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            
            var failedCount = await dbContext.OutboxMessages
                .Where(m => !m.Processed && m.RetryCount >= 3)
                .CountAsync(cancellationToken);
                
            var oldUnprocessedCount = await dbContext.OutboxMessages
                .Where(m => !m.Processed && m.CreatedAt < DateTime.UtcNow.AddHours(-1))
                .CountAsync(cancellationToken);
            
            if (failedCount > 10 || oldUnprocessedCount > 20)
            {
                return HealthCheckResult.Degraded(
                    $"Outbox has {failedCount} failed messages and {oldUnprocessedCount} old unprocessed messages");
            }
            
            return HealthCheckResult.Healthy("Outbox processing is healthy");
        }
    }
}
