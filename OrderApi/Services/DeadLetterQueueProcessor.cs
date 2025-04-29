using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OrderApi.Services
{
    public class DeadLetterQueueProcessor : BackgroundService
    {
        private readonly ILogger<DeadLetterQueueProcessor> _logger;
        private readonly IBusControl _bus;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _maxAge;
        private readonly TimeSpan _retryInterval;
        private readonly IAlertService _alertService;

        public DeadLetterQueueProcessor(
            ILogger<DeadLetterQueueProcessor> logger,
            IBusControl bus,
            IConfiguration configuration,
            IAlertService alertService)
        {
            _logger = logger;
            _bus = bus;
            _configuration = configuration;
            
            var dlqConfig = configuration.GetSection("Resilience:DeadLetter");
            _maxAge = TimeSpan.FromSeconds(dlqConfig.GetValue<int>("MaxAge", 86400));
            _retryInterval = TimeSpan.FromSeconds(dlqConfig.GetValue<int>("RetryInterval", 300));
            _alertService = alertService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _bus.StartAsync(stoppingToken);
                _logger.LogInformation("Dead Letter Queue Processor started");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Log the current state of DLQ processing
                        _logger.LogInformation("Checking for messages in DLQ...");
                        
                        // Wait for the next check interval
                        await Task.Delay(_retryInterval, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in DLQ processing loop");
                        await _alertService.SendWebhookAlertAsync("DLQ Processing Error", ex.Message, "");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Dead Letter Queue Processor failed to start");
                await _alertService.SendWebhookAlertAsync("DLQ Processor Critical Error", ex.Message, "");
            }
            finally
            {
                await _bus.StopAsync(stoppingToken);
            }
        }
    }
} 