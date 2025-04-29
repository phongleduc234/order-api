using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OrderApi.Shared.Configuration;
using OrderApi.Data;
using StackExchange.Redis;

namespace OrderApi.Services
{
    public class HealthCheckService : IHealthCheck
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<HealthCheckConfig> _healthCheckConfig;
        private readonly ILogger<HealthCheckService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public HealthCheckService(
            IHttpClientFactory httpClientFactory,
            IOptions<HealthCheckConfig> healthCheckConfig,
            ILogger<HealthCheckService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _httpClientFactory = httpClientFactory;
            _healthCheckConfig = healthCheckConfig;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Check database connection
                var dbCheck = await CheckDatabaseHealthAsync(cancellationToken);
                if (dbCheck.Status != HealthStatus.Healthy)
                {
                    _logger.LogWarning("Database health check failed: {Status}", dbCheck.Status);
                    return dbCheck;
                }

                // Check RabbitMQ connection
                var rabbitCheck = await CheckRabbitMqHealthAsync(cancellationToken);
                if (rabbitCheck.Status != HealthStatus.Healthy)
                {
                    _logger.LogWarning("RabbitMQ health check failed: {Status}", rabbitCheck.Status);
                    return rabbitCheck;
                }

                // Check Redis connection
                var redisCheck = await CheckRedisHealthAsync(cancellationToken);
                if (redisCheck.Status != HealthStatus.Healthy)
                {
                    _logger.LogWarning("Redis health check failed: {Status}", redisCheck.Status);
                    return redisCheck;
                }

                _logger.LogInformation("All health checks passed successfully");
                return HealthCheckResult.Healthy("All services are healthy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed with exception");
                return HealthCheckResult.Unhealthy("Health check failed", ex);
            }
        }

        private async Task<HealthCheckResult> CheckDatabaseHealthAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
                
                return canConnect
                    ? HealthCheckResult.Healthy("Database is healthy")
                    : HealthCheckResult.Unhealthy("Database connection failed");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database health check failed", ex);
            }
        }

        private async Task<HealthCheckResult> CheckRabbitMqHealthAsync(CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RabbitMq");
                client.Timeout = TimeSpan.FromSeconds(_healthCheckConfig.Value.RabbitMqTimeout);
                var response = await client.GetAsync("/api/health", cancellationToken);
                
                return response.IsSuccessStatusCode
                    ? HealthCheckResult.Healthy("RabbitMQ is healthy")
                    : HealthCheckResult.Unhealthy($"RabbitMQ health check failed: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("RabbitMQ health check failed", ex);
            }
        }

        private async Task<HealthCheckResult> CheckRedisHealthAsync(CancellationToken cancellationToken)
        {
            try
            {
                var redis = ConnectionMultiplexer.Connect(_healthCheckConfig.Value.RedisConnectionString);
                var db = redis.GetDatabase();
                var pong = await db.PingAsync();
                
                return pong.TotalMilliseconds < _healthCheckConfig.Value.RedisTimeout
                    ? HealthCheckResult.Healthy("Redis is healthy")
                    : HealthCheckResult.Unhealthy("Redis response time exceeded threshold");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis health check failed", ex);
            }
        }
    }
} 