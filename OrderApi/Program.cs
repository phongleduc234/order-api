using MassTransit;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OrderApi.Services;
using OrderApi.Shared;
using OrderService.Data;
using System.Net.Http.Headers;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Configure Entity Framework and PostgreSQL
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHealthChecks().AddCheck<OutboxHealthCheck>("outbox_health");

// Bind RabbitMQ configuration
var rabbitMqOptions = builder.Configuration.GetSection("RabbitMq").Get<RabbitMqOptions>();

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
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri($"rabbitmq://{rabbitMqOptions.Host}:{rabbitMqOptions.Port}/"), h =>
        {
            h.Username(rabbitMqOptions.UserName ?? "admin");
            h.Password(rabbitMqOptions.Password ?? "123456");
        });

        // Cấu hình global error handling
        cfg.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30)
        ));
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
        cfg.UseInMemoryOutbox(context);

        // Thêm vào cấu hình RabbitMQ
        cfg.ReceiveEndpoint("global-dead-letter-queue", e =>
        {
            e.ConfigureConsumer<DeadLetterConsumer>(context);
            e.Bind("order-compensated-dlq");
            e.Bind("order-created-dlq");
        });

        // Cấu hình từng endpoint
        cfg.ReceiveEndpoint("order-compensated", e =>
        {
            e.ConfigureConsumer<OrderConsumer>(context);
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.BindDeadLetterQueue("order-compensated-dlq", "order-compensated-dlx",
                dlq => dlq.Durable = true);
        });

        cfg.ReceiveEndpoint("order-created", e =>
        {
            e.ConfigureSaga<OrderSagaState>(context);
            e.BindDeadLetterQueue("order-created-dlq", "order-created-dlx",
                dlq => dlq.Durable = true);
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
