using MassTransit;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OrderApi.Services;
using OrderApi.Shared;
using OrderService.Data;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

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

// Bind RabbitMQ configuration
var rabbitMqOptions = builder.Configuration.GetSection("RabbitMq").Get<RabbitMqOptions>();

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri($"rabbitmq://{rabbitMqOptions.Host}:{rabbitMqOptions.Port}/"), h =>
        {
            h.Username(rabbitMqOptions.UserName ?? "admin");
            h.Password(rabbitMqOptions.Password ?? "123456");
        });

        cfg.ReceiveEndpoint("compensate-order", e =>
        {
            e.ConfigureConsumer<OrderConsumer>(context);
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.BindDeadLetterQueue("compensate-order-dlq", "compensate-order-dlx");
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
