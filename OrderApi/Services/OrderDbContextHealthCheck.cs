using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderApi.Data;
using System.Threading;
using System.Threading.Tasks;

namespace OrderApi.Services
{
    public class OrderDbContextHealthCheck : IHealthCheck
    {
        private readonly OrderDbContext _dbContext;
        private readonly ILogger<OrderDbContextHealthCheck> _logger;

        public OrderDbContextHealthCheck(
            OrderDbContext dbContext,
            ILogger<OrderDbContextHealthCheck> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
                
                if (canConnect)
                {
                    _logger.LogInformation("Database connection is healthy");
                    return HealthCheckResult.Healthy("Database connection is healthy");
                }

                _logger.LogWarning("Database connection failed");
                return HealthCheckResult.Unhealthy("Database connection failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return HealthCheckResult.Unhealthy("Database health check failed", ex);
            }
        }
    }
} 