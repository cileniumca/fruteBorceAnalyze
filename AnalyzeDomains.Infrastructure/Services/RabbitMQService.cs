using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;
using System.Threading.Channels;

namespace AnalyzeDomains.Infrastructure.Services;

public class RabbitMQService : IRabbitMQService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMQService> _logger;
    private readonly string _batchCompletedQueueName;
    private readonly string _completedEventsQueueName; // Single queue for both event types
    private readonly string _analyzeEvent;
    private volatile IConnection? _connection;
    private readonly object _connectionLock = new object();
    private readonly object _publishLock = new object();
    private readonly ConnectionFactory _connectionFactory;
    private volatile bool _disposed = false;

    private  IConnection _connectionNew;
    private IModel _channel;

    public RabbitMQService(IConfiguration configuration, ILogger<RabbitMQService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batchCompletedQueueName = _configuration.GetValue<string>("RabbitMQ:BatchCompletedQueueName") ?? "batch-processing-completed";
        _completedEventsQueueName = _configuration.GetValue<string>("RabbitMQ:CompletedEventsQueueName") ?? "forcequeue";
        _analyzeEvent = "analyzeforce";
        _connectionFactory = CreateConnectionFactory();
        _connectionNew = GetConnection();
        _channel = _connectionNew.CreateModel();
        _logger.LogInformation("RabbitMQ service initialized");
    }
    public async Task PublishEventAsync<T>(T eventData, string queueName, CancellationToken cancellationToken = default) where T : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));
        if (string.IsNullOrWhiteSpace(queueName)) throw new ArgumentException("Queue name must be provided", nameof(queueName));

        await Task.Yield(); // To maintain async signature without blocking

        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                PublishInternal(eventData, queueName);
                _logger.LogDebug("Published event {EventType} to queue {QueueName} on attempt {Attempt}",
                    typeof(T).Name, queueName, attempt);
                return; // Success, exit retry loop
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
            {
                _logger.LogWarning(ex, "Failed to publish event {EventType} to queue {QueueName} on attempt {Attempt}. Retrying in {Delay}ms",
                    typeof(T).Name, queueName, attempt, retryDelay.TotalMilliseconds);

                await Task.Delay(retryDelay, cancellationToken);
                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish event {EventType} to queue {QueueName} after {Attempts} attempts",
                    typeof(T).Name, queueName, attempt);
                throw;
            }
        }
    }
    public bool TestConnection()
    {

        _connectionNew = GetConnection();

        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));

        try
        {
            // Use the same synchronization pattern as PublishInternal
            lock (_publishLock)
            {
                var connection = GetConnection();
                using var channel = connection.CreateModel();
                // Test basic queue declaration to ensure full connectivity
                var testQueueName = $"test-connection-{Guid.NewGuid()}";
                channel.QueueDeclare(
                    queue: testQueueName,
                    durable: false,
                    exclusive: true,
                    autoDelete: true,
                    arguments: null);
                channel.QueueDelete(testQueueName);
            }

            _logger.LogInformation("RabbitMQ connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ connection test failed");
            return false;
        }
    }
    private static bool IsRetryableException(Exception ex)
    {
        return ex is RabbitMQ.Client.Exceptions.AlreadyClosedException ||
               ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException ||
               ex is System.Net.Sockets.SocketException ||
               ex is System.IO.IOException ||
               ex is TimeoutException;
    }
    public async Task PublishBatchCompletedEventAsync(CompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        await PublishEventAsync(eventData, _batchCompletedQueueName, cancellationToken);
        _logger.LogInformation("Published batch completed event");
    }    public async Task PublishAnalyzeEventAsync(AnalyzeEvent eventData, CancellationToken cancellationToken = default)
    {
        await PublishEventAsync(eventData, _analyzeEvent, cancellationToken);
        _logger.LogInformation("Published analyze event");
    }

    public async Task PublishAnalyzeEventsBatchAsync(IEnumerable<AnalyzeEvent> events, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        if (events == null) throw new ArgumentNullException(nameof(events));

        var eventsList = events.ToList();
        if (eventsList.Count == 0) return;

        await Task.Yield(); // To maintain async signature without blocking

        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                PublishBatchInternal(eventsList, _analyzeEvent);
                _logger.LogDebug("Published batch of {EventCount} analyze events on attempt {Attempt}",
                    eventsList.Count, attempt);
                return; // Success, exit retry loop
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
            {
                _logger.LogWarning(ex, "Failed to publish batch of {EventCount} analyze events on attempt {Attempt}. Retrying in {Delay}ms",
                    eventsList.Count, attempt, retryDelay.TotalMilliseconds);

                await Task.Delay(retryDelay, cancellationToken);
                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish batch of {EventCount} analyze events after {Attempts} attempts",
                    eventsList.Count, attempt);
                throw;
            }
        }
    }
    public async Task PublishBatchCompletedEventAsync(CompletedEvent eventData, List<WordPressUser> users, CancellationToken cancellationToken = default)
    {
        // Determine event type based on XML-RPC support
        EventType eventType = DetermineEventType(users);
        if (eventType is EventType.XmlRpcCompleted)
        {
            BaseCompletedEvent completedEvent = CreateCompletedEvent(eventData, eventType);

            await PublishEventAsync(completedEvent, _completedEventsQueueName, cancellationToken);
            _logger.LogInformation("Published {EventType} event for site {SiteId}",
                completedEvent.EventType, eventData.SiteId);

            BaseCompletedEvent completedEventWP = CreateCompletedEvent(eventData, EventType.WpLoginCompleted);

            await PublishEventAsync(completedEventWP, _completedEventsQueueName, cancellationToken);
            _logger.LogInformation("Published {EventType} event for site {SiteId}",
                completedEventWP.EventType, eventData.SiteId);

        }
        else if (eventType is EventType.WpLoginCompleted)
        {
            BaseCompletedEvent completedEvent = CreateCompletedEvent(eventData, eventType);
            await PublishEventAsync(completedEvent, _completedEventsQueueName, cancellationToken);
            _logger.LogInformation("Published {EventType} event for site {SiteId}",
                completedEvent.EventType, eventData.SiteId);
        }
        else
        {
            _logger.LogWarning("Unsupported event type detected for site {SiteId}", eventData.SiteId);
        }
    }

    private EventType DetermineEventType(List<WordPressUser> users)
    {
        // Check if XML-RPC is supported based on user detection methods
        bool supportsXmlRpc = users.Any(user => user.DetectionMethod == "XML-RPC");
        return supportsXmlRpc ? EventType.XmlRpcCompleted : EventType.WpLoginCompleted;
    }


    private BaseCompletedEvent CreateCompletedEvent(CompletedEvent eventData, EventType eventType)
    {
        return eventType switch
        {
            EventType.XmlRpcCompleted => new XmlRpcCompletedEvent
            {
                SiteId = eventData.SiteId,
                FullUrl = eventData.FullUrl,
                LoginPage = eventData.LoginPage,
                Login = eventData.Login
            },
            EventType.WpLoginCompleted => new WpLoginCompletedEvent
            {
                SiteId = eventData.SiteId,
                FullUrl = eventData.FullUrl,
                LoginPage = eventData.LoginPage,
                Login = eventData.Login
            },
            _ => throw new ArgumentException($"Unsupported event type: {eventType}")
        };
    }    private void PublishInternal<T>(T eventData, string queueName) where T : class
    {

       
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));

        // Declare queue with thread-safe operation
        _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            var message = JsonConvert.SerializeObject(eventData);
            var body = Encoding.UTF8.GetBytes(message);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = Guid.NewGuid().ToString();
            properties.Type = typeof(T).Name;
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Publish with confirmation for reliability
        _channel.ConfirmSelect();
        _channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: properties,
                body: body);

            // Wait for confirmation to ensure message was received
            if (!_channel.WaitForConfirms(TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException($"Failed to confirm message publication to queue {queueName}");
            }
        
    }

    private void PublishBatchInternal<T>(IList<T> events, string queueName) where T : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        if (events == null || events.Count == 0) return;

        // Declare queue with thread-safe operation
        _channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Enable publisher confirms for batch
        _channel.ConfirmSelect();

        // Publish all messages in the batch
        foreach (var eventData in events)
        {
            var message = JsonConvert.SerializeObject(eventData);
            var body = Encoding.UTF8.GetBytes(message);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = Guid.NewGuid().ToString();
            properties.Type = typeof(T).Name;
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            _channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: properties,
                body: body);
        }

        // Wait for confirmation of all messages
        if (!_channel.WaitForConfirms(TimeSpan.FromSeconds(30)))
        {
            throw new InvalidOperationException($"Failed to confirm batch publication of {events.Count} messages to queue {queueName}");
        }
    }
    private IConnection GetConnection()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));

        // Double-checked locking pattern for thread safety and performance
        if (_connection == null || !_connection.IsOpen)
        {
            lock (_connectionLock)
            {
                if (_connection == null || !_connection.IsOpen)
                {
                    try
                    {
                        // Dispose old connection if it exists
                        if (_connection != null)
                        {
                            try
                            {
                                _connection.Close();
                                _connection.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error disposing old RabbitMQ connection");
                            }
                        }

                        _connection = _connectionFactory.CreateConnection();
                        _logger.LogInformation("RabbitMQ connection established");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create RabbitMQ connection");
                        _connection = null;
                        throw;
                    }
                }
            }
        }

        return _connection!;
    }    private ConnectionFactory CreateConnectionFactory()
    {
        return new ConnectionFactory
        {
            HostName = _configuration.GetValue<string>("RabbitMQ:HostName") ?? "localhost",
            Port = _configuration.GetValue<int>("RabbitMQ:Port", 5672),
            UserName = _configuration.GetValue<string>("RabbitMQ:UserName") ?? "guest",
            Password = _configuration.GetValue<string>("RabbitMQ:Password") ?? "guest",
            VirtualHost = _configuration.GetValue<string>("RabbitMQ:VirtualHost") ?? "/",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5), // Faster recovery
            RequestedHeartbeat = TimeSpan.FromSeconds(30), // More frequent heartbeats
            RequestedConnectionTimeout = TimeSpan.FromSeconds(15), // Faster timeout
            // Additional settings for better performance and thread safety
            TopologyRecoveryEnabled = true,
            ContinuationTimeout = TimeSpan.FromSeconds(10), // Faster timeout
            HandshakeContinuationTimeout = TimeSpan.FromSeconds(5), // Faster timeout
            RequestedChannelMax = 5000 // Increased channel limit
        };
    }
    public Task<IEnumerable<SiteInfo>> ConsumeAnalyzeEventsAsync(int maxMessages, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        if (maxMessages <= 0) throw new ArgumentException("Max messages must be greater than 0", nameof(maxMessages));

        var consumedEvents = new List<SiteInfo>();
        
        try
        {
            // Declare the queue to ensure it exists
            _channel.QueueDeclare(
                queue: _analyzeEvent,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // Get basic information about the queue
            var queueInfo = _channel.QueueDeclarePassive(_analyzeEvent);
            var availableMessages = Math.Min((int)queueInfo.MessageCount, maxMessages);
            
            _logger.LogInformation("Attempting to consume {RequestedMessages} messages from queue {QueueName}. Available messages: {AvailableMessages}", 
                maxMessages, _analyzeEvent, queueInfo.MessageCount);            if (availableMessages == 0)
            {
                return Task.FromResult<IEnumerable<SiteInfo>>(consumedEvents);
            }

            // Consume messages one by one
            for (int i = 0; i < availableMessages && !cancellationToken.IsCancellationRequested; i++)
            {
                var result = _channel.BasicGet(_analyzeEvent, false); // Don't auto-ack
                
                if (result != null)
                {
                    try
                    {
                        var messageBody = Encoding.UTF8.GetString(result.Body.ToArray());
                        
                        // Try to deserialize as SiteInfo first
                        SiteInfo? siteInfo = null;
                        try
                        {
                            siteInfo = JsonConvert.DeserializeObject<SiteInfo>(messageBody);
                        }
                        catch (JsonException)
                        {
                            // If that fails, try to deserialize as AnalyzeEvent and convert
                            try
                            {
                                var analyzeEvent = JsonConvert.DeserializeObject<AnalyzeEvent>(messageBody);
                                if (analyzeEvent != null)
                                {
                                    siteInfo = new SiteInfo
                                    {
                                        SiteId = analyzeEvent.SiteId,
                                        Domain = analyzeEvent.Domain
                                    };
                                }
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "Failed to deserialize message as SiteInfo or AnalyzeEvent. Message body: {MessageBody}", messageBody);
                            }
                        }

                        if (siteInfo != null && !string.IsNullOrEmpty(siteInfo.Domain))
                        {
                            consumedEvents.Add(siteInfo);
                            _channel.BasicAck(result.DeliveryTag, false); // Acknowledge successful processing
                            _logger.LogInformation("Successfully consumed and processed message {DeliveryTag} for site {SiteId}", 
                                result.DeliveryTag, siteInfo.SiteId);
                        }
                        else
                        {
                            _logger.LogInformation("Received invalid SiteInfo message. Rejecting message {DeliveryTag}", result.DeliveryTag);
                            _channel.BasicReject(result.DeliveryTag, false); // Reject and don't requeue
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message {DeliveryTag}. Rejecting message.", result.DeliveryTag);
                        _channel.BasicReject(result.DeliveryTag, false); // Reject and don't requeue
                    }
                }
                else
                {
                    // No more messages available
                    break;
                }
            }

            _logger.LogInformation("Successfully consumed {ConsumedCount} messages from queue {QueueName}", 
                consumedEvents.Count, _analyzeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming messages from queue {QueueName}", _analyzeEvent);
            throw;
        }

        return Task.FromResult<IEnumerable<SiteInfo>>(consumedEvents);
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_connectionLock)
        {
            if (_disposed) return; // Double-check inside lock

            _disposed = true;

            try
            {
                if (_connection != null)
                {
                    if (_connection.IsOpen)
                    {
                        _connection.Close();
                    }
                    _connection.Dispose();
                    _connection = null;
                }
                _logger.LogInformation("RabbitMQ connection disposed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing RabbitMQ connection");
            }
        }
    }
}
