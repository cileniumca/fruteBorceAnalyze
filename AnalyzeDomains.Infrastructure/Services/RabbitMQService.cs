using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class RabbitMQService : IRabbitMQService, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RabbitMQService> _logger;
        private readonly string _batchCompletedQueueName;
        private readonly object _lockObject = new object();

        public RabbitMQService(IConfiguration configuration, ILogger<RabbitMQService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _batchCompletedQueueName = _configuration.GetValue<string>("RabbitMQ:BatchCompletedQueueName") ?? "batch-processing-completed";
            
            _logger.LogInformation("RabbitMQ service initialized");
        }

        private IConnection CreateConnection()
        {
            var factory = new ConnectionFactory()
            {
                HostName = _configuration.GetValue<string>("RabbitMQ:HostName") ?? "localhost",
                Port = _configuration.GetValue<int>("RabbitMQ:Port", 5672),
                UserName = _configuration.GetValue<string>("RabbitMQ:UserName") ?? "guest",
                Password = _configuration.GetValue<string>("RabbitMQ:Password") ?? "guest",
                VirtualHost = _configuration.GetValue<string>("RabbitMQ:VirtualHost") ?? "/",
            };

            return factory.CreateConnection();
        }

        public async Task PublishEventAsync<T>(T eventData, string queueName, CancellationToken cancellationToken = default) where T : class
        {
            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        using var connection = CreateConnection();
                        using var channel = connection.CreateModel();

                        // Declare queue if it doesn't exist
                        channel.QueueDeclare(
                            queue: queueName,
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);

                        var message = JsonConvert.SerializeObject(eventData, Formatting.None);
                        var body = Encoding.UTF8.GetBytes(message);

                        var properties = channel.CreateBasicProperties();
                        properties.Persistent = true;
                        properties.MessageId = Guid.NewGuid().ToString();
                        properties.Type = typeof(T).Name;
                        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                        channel.BasicPublish(
                            exchange: "",
                            routingKey: queueName,
                            basicProperties: properties,
                            body: body);

                        _logger.LogDebug("Published event {EventType} to queue {QueueName}", typeof(T).Name, queueName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish event {EventType} to queue {QueueName}", typeof(T).Name, queueName);
                        // Don't rethrow, allow the application to continue
                    }
                }
            }, cancellationToken);
        }

        public async Task PublishBatchCompletedEventAsync(CompletedEvent eventData, CancellationToken cancellationToken = default)
        {
            await PublishEventAsync(eventData, _batchCompletedQueueName, cancellationToken);
            _logger.LogInformation("Published batch completed event");
        }

        public void Dispose()
        {
            _logger.LogInformation("RabbitMQ service disposed");
        }
    }
}