// OrderApi/Controllers/TestingController.cs
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using OrderApi.Extensions;
using OrderApi.Services;
using OrderApi.Shared;
using OrderService.Data;

namespace OrderApi.Controllers
{
    [ApiController]
    [Route("api/testing")]
    public class TestingController : ControllerBase
    {
        private readonly OrderDbContext _context;
        private readonly IBus _bus;
        private readonly ILogger<TestingController> _logger;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        public TestingController(
            OrderDbContext context,
            IBus bus,
            ILogger<TestingController> logger,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _context = context;
            _bus = bus;
            _logger = logger;
        }

        // 1. Simulate a failed order that needs compensation
        [HttpPost("simulate-failed-payment")]
        public async Task<IActionResult> SimulateFailedPayment()
        {
            try
            {
                // Create a test order
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    ProductId = "test-product",
                    Quantity = 1,
                    Amount = 99.99m,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                // Create and save compensation event
                var compensateEvent = new OrderCompensated(Guid.NewGuid(), order.Id);
                await _context.SaveEventToOutboxAsync(compensateEvent);

                return Ok(new
                {
                    Message = "Failed payment simulation successful",
                    OrderId = order.Id,
                    OutboxMessageId = compensateEvent.CorrelationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error simulating failed payment");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        // 2. Simulate a permanently failing outbox message
        [HttpPost("simulate-failing-outbox")]
        public async Task<IActionResult> SimulateFailingOutbox()
        {
            try
            {
                // Create a malformed outbox message that will fail when processed
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "NonExistentType, AssemblyThatDoesNotExist", // This will cause Type.GetType to fail
                    EventData = "{ \"this\": \"is invalid json\" :",  // Malformed JSON
                    CreatedAt = DateTime.UtcNow,
                    Processed = false,
                    RetryCount = 0
                };

                _context.OutboxMessages.Add(outboxMessage);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Message = "Failing outbox message created successfully",
                    OutboxMessageId = outboxMessage.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating failing outbox message");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        // 3. Test the Dead Letter Queue by publishing a message that will fail
        [HttpPost("test-dlq")]
        public async Task<IActionResult> TestDeadLetterQueue()
        {
            try
            {
                // Create a special event class that will trigger DLQ
                var dlqTestEvent = new
                {
                    Id = Guid.NewGuid(),
                    TestMessage = "This message will be sent to DLQ",
                    ThrowException = true, // This flag can be used in a consumer to throw an exception
                    CreatedAt = DateTime.UtcNow
                };

                // Publish directly to MassTransit
                await _bus.Publish(dlqTestEvent);

                return Ok(new
                {
                    Message = "Message sent to test DLQ functionality",
                    EventId = dlqTestEvent.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing DLQ");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        // 4. Create multiple outbox messages to test bulk processing
        [HttpPost("generate-test-outbox-messages")]
        public async Task<IActionResult> GenerateTestOutboxMessages([FromQuery] int count = 10)
        {
            try
            {
                var createdIds = new List<Guid>();

                for (int i = 0; i < count; i++)
                {
                    var orderId = Guid.NewGuid();

                    var order = new Order
                    {
                        Id = orderId,
                        ProductId = $"test-product-{i}",
                        Quantity = i + 1,
                        Amount = (i + 1) * 10.0m,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Orders.Add(order);

                    // Create order events in outbox
                    var orderCreatedEvent = new OrderCreated(Guid.NewGuid(), orderId);

                    var outboxMessage = new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(OrderCreated).AssemblyQualifiedName,
                        EventData = JsonConvert.SerializeObject(orderCreatedEvent),
                        CreatedAt = DateTime.UtcNow,
                        Processed = false,
                        RetryCount = 0
                    };

                    _context.OutboxMessages.Add(outboxMessage);
                    createdIds.Add(outboxMessage.Id);
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Message = $"Created {count} test outbox messages",
                    OutboxMessageIds = createdIds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating test outbox messages");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        // 5. Simulate manual intervention scenario
        [HttpPost("simulate-manual-intervention")]
        public async Task<IActionResult> SimulateManualIntervention()
        {
            try
            {
                // Create an outbox message with high retry count
                var orderId = Guid.NewGuid();
                var orderCreatedEvent = new OrderCreated(Guid.NewGuid(), orderId);

                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = typeof(OrderCreated).AssemblyQualifiedName,
                    EventData = JsonConvert.SerializeObject(orderCreatedEvent),
                    CreatedAt = DateTime.UtcNow.AddHours(-2), // Backdate by 2 hours
                    Processed = false,
                    RetryCount = 4  // Just below the max retry threshold
                };

                _context.OutboxMessages.Add(outboxMessage);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Message = "Created message requiring manual intervention",
                    OutboxMessageId = outboxMessage.Id,
                    Instructions = "This message has a high retry count and is old. It should trigger alerts and appear in health check as requiring attention."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error simulating manual intervention");
                return StatusCode(500, new { Error = ex.Message });
            }
        }
        // Thêm endpoint test mail
        [HttpPost("test-email")]
        public async Task<IActionResult> TestEmail([FromBody] EmailTestRequest request)
        {
            if (string.IsNullOrEmpty(request?.To))
            {
                return BadRequest("Email recipient is required");
            }

            try
            {
                var subject = request.Subject ?? "Test Email from Order API";
                var body = request.Body ?? $"This is a test email sent from OrderAPI at {DateTime.Now}. SMTP configuration is working correctly.";
                var isHtml = request.IsHtml ?? false;

                var success = await _emailService.SendEmailAsync(
                    request.To,
                    subject,
                    body,
                    isHtml);

                if (success)
                {
                    return Ok(new
                    {
                        Message = "Test email sent successfully",
                        To = request.To,
                        Subject = subject,
                        SmtpSettings = new
                        {
                            Host = _configuration.GetValue<string>("SmtpMail:Host"),
                            Port = _configuration.GetValue<int>("SmtpMail:Port"),
                            User = _configuration.GetValue<string>("SmtpMail:User")
                        }
                    });
                }
                else
                {
                    return StatusCode(500, new { Error = "Failed to send email. Check logs for details." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test email");
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }

    public class EmailTestRequest
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public bool? IsHtml { get; set; }
    }
}
