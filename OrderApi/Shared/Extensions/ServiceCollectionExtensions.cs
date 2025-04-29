using OrderApi.Shared.Configuration;
using Polly;

namespace OrderApi.Shared.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddResiliencePolicies(this IServiceCollection services, IConfiguration configuration)
        {
            var resilienceConfig = configuration.GetSection("Resilience").Get<ResilienceConfig>();

            // Create retry policy
            var retryPolicy = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    resilienceConfig.Retry.Count,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(resilienceConfig.Retry.BaseDelay, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        var logger = services.BuildServiceProvider().GetService<ILogger<HttpClient>>();
                        logger?.LogWarning(
                            "Retry {RetryCount} after {Delay}ms due to: {Exception}",
                            retryCount,
                            timeSpan.TotalMilliseconds,
                            exception);
                    });

            // Create circuit breaker policy
            var circuitBreakerPolicy = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .CircuitBreakerAsync(
                    resilienceConfig.CircuitBreaker.FailureThreshold,
                    TimeSpan.FromSeconds(resilienceConfig.CircuitBreaker.DurationOfBreak),
                    onBreak: (exception, duration) =>
                    {
                        var logger = services.BuildServiceProvider().GetService<ILogger<HttpClient>>();
                        logger?.LogWarning(
                            "Circuit breaker opened for {Duration} due to: {Exception}",
                            duration,
                            exception);
                    },
                    onReset: () =>
                    {
                        var logger = services.BuildServiceProvider().GetService<ILogger<HttpClient>>();
                        logger?.LogInformation("Circuit breaker reset");
                    },
                    onHalfOpen: () =>
                    {
                        var logger = services.BuildServiceProvider().GetService<ILogger<HttpClient>>();
                        logger?.LogInformation("Circuit breaker half-open");
                    });

            // Register policies with HttpClient
            services.AddHttpClient("ResilientHttpClient", client =>
            {
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy);

            return services;
        }

        public static IServiceCollection AddHealthCheckConfig(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<HealthCheckConfig>(configuration.GetSection("HealthChecks"));
            return services;
        }
    }
} 