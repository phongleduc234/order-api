using MassTransit;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OrderApi.Consumers;
using OrderApi.Services;
using OrderApi.Shared;
using OrderService.Data;
using StackExchange.Redis;
using System.Net.Http.Headers;
using System.Reflection;

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
        ConnectTimeout = 5000
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
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
// Đăng ký Outbox Background Service
builder.Services.AddHostedService<OutboxPublisherService>();
// Đăng ký Alert Service
builder.Services.AddSingleton<IAlertService, AlertService>();
builder.Services.AddHealthChecks().AddCheck<OutboxHealthCheck>("outbox_health");

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order API",
        Version = "v1",
        Description = "API for order management with Outbox Pattern and DLQ support",
        Contact = new OpenApiContact
        {
            Name = "Development Team",
            Email = "jun8124@gmail.com"
        }
    });

    // Add XML comments support
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Group endpoints by controller
    options.TagActionsBy(api => new[] { api.GroupName ?? api.ActionDescriptor.RouteValues["controller"] });
});
// Trong Program.cs, sửa đổi cấu hình MassTransit
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderConsumer>();
    x.AddConsumer<DeadLetterConsumer>();
    // Thêm consumer cho các event từ Saga nếu cần
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

        cfg.UseDelayedRedelivery(r => r.Intervals(
             TimeSpan.FromMinutes(1),
             TimeSpan.FromMinutes(5),
             TimeSpan.FromMinutes(15)
         ));
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

        // Endpoint để phát hành OrderCreated (không cần ConfigureSaga)
        cfg.ReceiveEndpoint("order-created", e =>
        {
            // Không cần ConfigureSaga ở đây vì OrderApi không quản lý Saga
            // OrdersController sẽ publish OrderCreated
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.BindDeadLetterQueue("order-created-dlq", "order-created-dlx", x => {
                x.Durable = true;
            });
        });

        // Endpoint để nhận CompensateOrder command từ Saga
        cfg.ReceiveEndpoint("compensate-order", e =>
        {
            e.ConfigureConsumer<OrderConsumer>(context); // Đúng là ConfigureConsumer
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.BindDeadLetterQueue("compensate-order-dlq", "compensate-order-dlx", x => {
                x.Durable = true;
            });
        });

        // Endpoint để nhận OrderFulfilled từ Saga
        cfg.ReceiveEndpoint("order-fulfilled", e =>
        {
            e.ConfigureConsumer<OrderFulfilledConsumer>(context);
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.BindDeadLetterQueue("order-fulfilled-dlq", "order-fulfilled-dlx", x => {
                x.Durable = true;
            });
        });

        // Endpoint cho DeadLetterConsumer
        cfg.ReceiveEndpoint("order-dead-letter-queue", e =>
        {
            e.ConfigureConsumer<DeadLetterConsumer>(context);
            // Bind các dead letter queues
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

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API V1");
    c.RoutePrefix = "swagger";
});

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.MapHealthChecks("/health");
app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.Run();
