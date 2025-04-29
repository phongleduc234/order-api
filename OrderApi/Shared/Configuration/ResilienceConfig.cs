namespace OrderApi.Shared.Configuration
{
    public class ResilienceConfig
    {
        public RetryConfig Retry { get; set; }
        public CircuitBreakerConfig CircuitBreaker { get; set; }
        public TimeoutConfig Timeout { get; set; }
    }

    public class RetryConfig
    {
        public int Count { get; set; } = 3;
        public int BaseDelay { get; set; } = 2; // seconds
        public int MaxDelay { get; set; } = 30; // seconds
    }

    public class CircuitBreakerConfig
    {
        public int FailureThreshold { get; set; } = 3;
        public int SamplingDuration { get; set; } = 30; // seconds
        public int MinimumThroughput { get; set; } = 5;
        public int DurationOfBreak { get; set; } = 30; // seconds
    }

    public class TimeoutConfig
    {
        public int Seconds { get; set; } = 30;
    }
} 