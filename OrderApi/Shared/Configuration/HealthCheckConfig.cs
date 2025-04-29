namespace OrderApi.Shared.Configuration
{
    public class HealthCheckConfig
    {
        public string RedisConnectionString { get; set; }
        public int RedisTimeout { get; set; } = 1000; // milliseconds
        public int DatabaseTimeout { get; set; } = 30; // seconds
        public int RabbitMqTimeout { get; set; } = 30; // seconds
        public int HealthCheckInterval { get; set; } = 60; // seconds
        public int FailedThreshold { get; set; } = 3;
        public int StaleThreshold { get; set; } = 5;
    }
} 