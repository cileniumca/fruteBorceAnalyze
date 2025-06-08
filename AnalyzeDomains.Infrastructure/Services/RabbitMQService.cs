using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;

namespace AnalyzeDomains.Infrastructure.Services;

public class RabbitMQService : IRabbitMQService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMQService> _logger;
    private readonly string _batchCompletedQueueName;

    public RabbitMQService(IConfiguration configuration, ILogger<RabbitMQService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batchCompletedQueueName = _configuration.GetValue<string>("RabbitMQ:BatchCompletedQueueName") ?? "batch-processing-completed";

        _logger.LogInformation("RabbitMQ service initialized");
    }

    public async Task PublishEventAsync<T>(T eventData, string queueName, CancellationToken cancellationToken = default) where T : class
    {
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));
        if (string.IsNullOrWhiteSpace(queueName)) throw new ArgumentException("Queue name must be provided", nameof(queueName));

        await Task.Yield(); // To maintain async signature without blocking

        try
        {
            PublishInternal(eventData, queueName);
            _logger.LogDebug("Published event {EventType} to queue {QueueName}", typeof(T).Name, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} to queue {QueueName}", typeof(T).Name, queueName);
        }
    }

    public async Task PublishBatchCompletedEventAsync(CompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        await PublishEventAsync(eventData, _batchCompletedQueueName, cancellationToken);
        _logger.LogInformation("Published batch completed event");
    }

    private void PublishInternal<T>(T eventData, string queueName) where T : class
    {
        using var connection = CreateConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var message = JsonConvert.SerializeObject(eventData);
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
    }

    private IConnection CreateConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration.GetValue<string>("RabbitMQ:HostName") ?? "localhost",
            Port = _configuration.GetValue<int>("RabbitMQ:Port", 5672),
            UserName = _configuration.GetValue<string>("RabbitMQ:UserName") ?? "guest",
            Password = _configuration.GetValue<string>("RabbitMQ:Password") ?? "guest",
            VirtualHost = _configuration.GetValue<string>("RabbitMQ:VirtualHost") ?? "/"
        };

        return factory.CreateConnection();
    }

    public void Dispose()
    {
        _logger.LogInformation("RabbitMQ service disposed");
        // Nothing to dispose currently; connections/channels are scoped per publish
    }
}
