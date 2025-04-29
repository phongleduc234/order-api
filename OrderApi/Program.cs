using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderApi.Consumers;
using OrderApi.Data;
using OrderApi.Services;
using OrderApi.Shared;
using OrderApi.Shared.Extensions;
using OrderApi.Shared.Middleware;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// Configure Entity Framework and PostgreSQL
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Redis connection
builder.Services.AddSingleton(sp =>
{
    var redisConfig = builder.Configuration.GetSection("Redis");
    var host = redisConfig["Host"] ?? "localhost";
    var port = redisConfig["Port"] ?? "6379";
    var password = redisConfig["Password"] ?? "";

    var configOptions = new ConfigurationOptions
    {
        AbortOnConnectFail = false,
        ConnectRetry = 3,
        ConnectTimeout = 10000,
        SyncTimeout = 10000,
        AsyncTimeout = 10000
    };

    configOptions.EndPoints.Add($"{host}:{port}");

    if (!string.IsNullOrEmpty(password))
    {
        configOptions.Password = password;
    }
    return ConnectionMultiplexer.Connect(configOptions);
});

// Register IHttpClientFactory
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("PaymentService", client =>
{
    client.BaseAddress = new Uri("http://payment-api-service:8081");
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

// Register background services
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService<DeadLetterQueueProcessor>();

// Register services
builder.Services.AddSingleton<IAlertService, AlertService>();
builder.Services.AddScoped<IDeadLetterQueueHandler, DeadLetterQueueHandler>();

// Configure health checks
builder.Services.AddHealthChecks()
    .AddCheck<OutboxHealthCheck>("outbox_health")
    .AddCheck<OrderApi.Services.HealthCheckService>("system_health", tags: new[] { "system" })
    .AddCheck<OrderDbContextHealthCheck>("database_health", tags: new[] { "database" })
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        name: "postgresql",
        tags: new[] { "database" },
        timeout: TimeSpan.FromSeconds(30))
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis"),
        name: "redis",
        tags: new[] { "cache" },
        timeout: TimeSpan.FromSeconds(30))
    .AddRabbitMQ(
        rabbitConnectionString: $"amqp://{builder.Configuration["RabbitMq:UserName"]}:{builder.Configuration["RabbitMq:Password"]}@{builder.Configuration["RabbitMq:Host"]}:{builder.Configuration["RabbitMq:Port"]}",
        name: "rabbitmq",
        tags: new[] { "message-broker" },
        timeout: TimeSpan.FromSeconds(30));

// Configure resilience policies
builder.Services.AddResiliencePolicies(builder.Configuration);

// Configure health check settings
builder.Services.AddHealthCheckConfig(builder.Configuration);

// Trong Program.cs, sửa đổi cấu hình MassTransit
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderConsumer>();
    x.AddConsumer<DeadLetterConsumer>();
    x.AddConsumer<OrderFulfilledConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitConfig = builder.Configuration.GetSection("RabbitMq");
        var host = rabbitConfig["Host"] ?? "localhost";
        var port = rabbitConfig.GetValue<int>("Port", 5672);
        var username = rabbitConfig["UserName"] ?? "guest";
        var password = rabbitConfig["Password"] ?? "guest";

        cfg.Host(new Uri($"rabbitmq://{host}:{port}"), h =>
        {
            h.Username(username);
            h.Password(password);
        });

        // Configure retry policy at bus level
        var retryConfig = builder.Configuration.GetSection("Resilience:Retry");
        cfg.UseMessageRetry(r => r.Interval(
            retryConfig.GetValue<int>("Count", 3),
            TimeSpan.FromSeconds(retryConfig.GetValue<int>("BaseDelay", 2))
        ));

        // Configure endpoints with DLQ
        cfg.ReceiveEndpoint("order-created", e =>
        {
            e.ConfigureConsumer<OrderConsumer>(context);
            e.BindDeadLetterQueue("order-created-dlq", "order-created-dlx", x => x.Durable = true);
            
            // Configure circuit breaker for this endpoint
            var circuitBreakerConfig = builder.Configuration.GetSection("Resilience:CircuitBreaker");
            e.UseCircuitBreaker(cb =>
            {
                cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                cb.TripThreshold = circuitBreakerConfig.GetValue<int>("FailureThreshold", 3);
                cb.ActiveThreshold = circuitBreakerConfig.GetValue<int>("MinimumThroughput", 5);
                cb.ResetInterval = TimeSpan.FromSeconds(circuitBreakerConfig.GetValue<int>("DurationOfBreak", 30));
            });
        });

        // Configure compensate-order endpoint
        cfg.ReceiveEndpoint("compensate-order", e =>
        {
            e.ConfigureConsumer<OrderConsumer>(context);
            e.BindDeadLetterQueue("compensate-order-dlq", "compensate-order-dlx", x => x.Durable = true);
        });

        // Configure order-fulfilled endpoint
        cfg.ReceiveEndpoint("order-fulfilled", e =>
        {
            e.ConfigureConsumer<OrderFulfilledConsumer>(context);
            e.BindDeadLetterQueue("order-fulfilled-dlq", "order-fulfilled-dlx", x => x.Durable = true);
        });

        // Configure dead letter queue endpoint
        cfg.ReceiveEndpoint("order-dead-letter-queue", e =>
        {
            e.ConfigureConsumer<DeadLetterConsumer>(context);
            e.Bind("compensate-order-dlq");
            e.Bind("order-fulfilled-dlq");
        });
    });
});

var app = builder.Build();

// Apply database migrations automatically
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    dbContext.Database.Migrate();
}

// Configure middleware pipeline
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Add global exception handling
app.UseMiddleware<GlobalExceptionHandler>();

// Add validation middleware
app.UseMiddleware<ValidationMiddleware>();

// Configure Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API V1");
    c.RoutePrefix = "swagger";
});

// Configure health checks
app.MapHealthChecks("/health");

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();
