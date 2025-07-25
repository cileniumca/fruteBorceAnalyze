using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using SystemTextJson = System.Text.Json;

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
    private readonly ConnectionFactory _connectionFactory;
    private volatile bool _disposed = false;

    // Fast serialization configuration to maintain compatibility with Newtonsoft.Json format
    private static readonly SystemTextJson.JsonSerializerOptions _fastJsonOptions = new()
    {
        PropertyNamingPolicy = SystemTextJson.JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = SystemTextJson.Serialization.JsonIgnoreCondition.WhenWritingNull
    };// Channel pooling and management optimization
    private readonly SemaphoreSlim _channelSemaphore = new SemaphoreSlim(50, 50); // Increased concurrent channel usage
    private readonly ConcurrentQueue<IModel> _channelPool = new();
    private const int MaxChannelPoolSize = 50;
    public RabbitMQService(IConfiguration configuration, ILogger<RabbitMQService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batchCompletedQueueName = _configuration.GetValue<string>("RabbitMQ:BatchCompletedQueueName") ?? "batch-processing-completed";
        _completedEventsQueueName = _configuration.GetValue<string>("RabbitMQ:CompletedEventsQueueName") ?? "xmlRPCQueue";
        _analyzeEvent = "analyzeQueue";
        _connectionFactory = CreateConnectionFactory();
        _logger.LogInformation("RabbitMQ service initialized");
    }
    public async Task PublishEventAsync<T>(T eventData, string queueName, CancellationToken cancellationToken = default) where T : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));
        if (string.IsNullOrWhiteSpace(queueName)) throw new ArgumentException("Queue name must be provided", nameof(queueName));

        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await PublishInternalAsync(eventData, queueName);
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
    }    public bool TestConnection()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));

        try
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
    }
    public async Task PublishAnalyzeEventAsync(AnalyzeEvent eventData, CancellationToken cancellationToken = default)
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

        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await PublishBatchInternalAsync(eventsList, _analyzeEvent);
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

    public async Task PublishBatchCompletedEventsAsync(List<CompletedEvent> events, List<WordPressUser> users, CancellationToken cancellationToken = default)
    {
        if (events == null || events.Count == 0) return;

        // Determine event type based on XML-RPC support (same for all events from same domain)
<<<<<<< HEAD
       
=======

>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a

        // Create all events to publish
        var eventsToPublish = new List<BaseCompletedEvent>();

        foreach (var eventData in events)
<<<<<<< HEAD
        {          
                // Add both XML-RPC and WP Login events for XML-RPC capable sites
                eventsToPublish.Add(CreateCompletedEvent(eventData, EventType.XmlRpcCompleted));
            
=======
        {
            // Add both XML-RPC and WP Login events for XML-RPC capable sites
            eventsToPublish.Add(CreateCompletedEvent(eventData, EventType.XmlRpcCompleted));

>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
        }

        if (eventsToPublish.Count > 0)
        {
            // Publish all events in a single batch operation
            await PublishEventsBatchAsync(eventsToPublish, _completedEventsQueueName, cancellationToken);

            _logger.LogInformation("Published batch of {EventCount} completed events for {SiteCount} sites with event type xmlRPC",
                eventsToPublish.Count, events.Count);
        }
    }
    private async Task PublishEventsBatchAsync<T>(List<T> events, string queueName, CancellationToken cancellationToken) where T : class
    {
        if (events == null || events.Count == 0) return;

        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await PublishBatchInternalAsync(events, queueName);
                _logger.LogDebug("Published batch of {EventCount} events to queue {QueueName} on attempt {Attempt}",
                    events.Count, queueName, attempt);
                return; // Success, exit retry loop
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
            {
                _logger.LogWarning(ex, "Failed to publish batch of {EventCount} events to queue {QueueName} on attempt {Attempt}. Retrying in {Delay}ms",
                    events.Count, queueName, attempt, retryDelay.TotalMilliseconds);

                await Task.Delay(retryDelay, cancellationToken);
                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish batch of {EventCount} events to queue {QueueName} after {Attempts} attempts",
                    events.Count, queueName, attempt);
                throw;
            }
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
            _ => throw new ArgumentException($"Unsupported event type: {eventType}")
        };
    }
    private async Task PublishInternalAsync<T>(T eventData, string queueName) where T : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        var channel = await GetChannelAsync();
        try
        {
            // Declare queue with thread-safe operation
            channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            var message = FastSerialize(eventData);
            var body = Encoding.UTF8.GetBytes(message);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = Guid.NewGuid().ToString();
            properties.Type = typeof(T).Name;
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // Publish with confirmation for reliability
            channel.ConfirmSelect();
            channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: properties,
                body: body);            // Wait for confirmation to ensure message was received
            if (!channel.WaitForConfirms(TimeSpan.FromSeconds(2))) // Reduced timeout
            {
                throw new InvalidOperationException($"Failed to confirm message publication to queue {queueName}");
            }
        }
        finally
        {
            ReturnChannel(channel);
        }
    }
    private async Task PublishBatchInternalAsync<T>(IList<T> events, string queueName) where T : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        if (events == null || events.Count == 0) return;

        var channel = await GetChannelAsync();
        try
        {
            // Declare queue with thread-safe operation
            channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // Enable publisher confirms for batch
            channel.ConfirmSelect();

            // Use batch publishing for better performance
            var batch = channel.CreateBasicPublishBatch();
            foreach (var eventData in events)
            {
                var message = FastSerialize(eventData);
                var body = Encoding.UTF8.GetBytes(message);

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.MessageId = Guid.NewGuid().ToString();
                properties.Type = typeof(T).Name;
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                batch.Add("", queueName, false, properties, new ReadOnlyMemory<byte>(body));
            }

            // Publish all messages in a single batch operation
            batch.Publish();

            // Wait for confirmation of all messages (reduced timeout for better performance)
            if (!channel.WaitForConfirms(TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException($"Failed to confirm batch publication of {events.Count} messages to queue {queueName}");
            }
        }
        finally
        {
            ReturnChannel(channel);
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
<<<<<<< HEAD
                        }                        _connection = _connectionFactory.CreateConnection();
                        
=======
                        }
                        _connection = _connectionFactory.CreateConnection();

>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
                        // Add connection event handlers for proper channel pool management
                        _connection.ConnectionShutdown += OnConnectionShutdown;
                        _connection.ConnectionBlocked += OnConnectionBlocked;
                        _connection.ConnectionUnblocked += OnConnectionUnblocked;
<<<<<<< HEAD
                        
=======

>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
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
<<<<<<< HEAD
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10), // Slower recovery
=======
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5), // Faster recovery
>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
            RequestedHeartbeat = TimeSpan.FromSeconds(60), // Longer heartbeats to prevent disconnections
            RequestedConnectionTimeout = TimeSpan.FromSeconds(30), // Longer timeout to prevent timeout exceptions
            // Additional settings for better performance and thread safety
            TopologyRecoveryEnabled = true,
            ContinuationTimeout = TimeSpan.FromSeconds(20), // Longer timeout
            HandshakeContinuationTimeout = TimeSpan.FromSeconds(10), // Longer timeout
<<<<<<< HEAD
            RequestedChannelMax = 5000 // Increased channel limit
=======
            RequestedChannelMax = 5000, // Increased channel limit
            // Performance optimizations
            DispatchConsumersAsync = true // Enable async consumer dispatching
>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
        };
    }// Connection event handlers for proper resource management
    private void OnConnectionShutdown(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning("RabbitMQ connection shutdown: {Reason}", e.ReplyText);
        ClearChannelPool();
    }

    private void OnConnectionBlocked(object? sender, ConnectionBlockedEventArgs e)
    {
        _logger.LogWarning("RabbitMQ connection blocked: {Reason}", e.Reason);
    }

    private void OnConnectionUnblocked(object? sender, EventArgs e)
    {
        _logger.LogInformation("RabbitMQ connection unblocked");
    }

    // Clear all channels from pool when connection issues occur
    private void ClearChannelPool()
    {
        while (_channelPool.TryDequeue(out var channel))
        {
            try
            {
                channel?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error disposing channel during pool cleanup: {Error}", ex.Message);
            }
        }
        _logger.LogDebug("Channel pool cleared due to connection issues");
    }
    public async Task<IEnumerable<SiteInfo>> ConsumeAnalyzeEventsAsync(int maxMessages, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQService));
        if (maxMessages <= 0) throw new ArgumentException("Max messages must be greater than 0", nameof(maxMessages));

        var consumedEvents = new List<SiteInfo>();
        var channel = await GetChannelAsync();

        try
        {
            // Declare the queue to ensure it exists
            channel.QueueDeclare(
                queue: _analyzeEvent,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // Get basic information about the queue
            var queueInfo = channel.QueueDeclarePassive(_analyzeEvent);
            var availableMessages = Math.Min((int)queueInfo.MessageCount, maxMessages);

            _logger.LogInformation("Attempting to consume {RequestedMessages} messages from queue {QueueName}. Available messages: {AvailableMessages}",
                maxMessages, _analyzeEvent, queueInfo.MessageCount);

            if (availableMessages == 0)
            {
                return consumedEvents;
            }

            // Consume messages one by one
            for (int i = 0; i < availableMessages && !cancellationToken.IsCancellationRequested; i++)
            {
                var result = channel.BasicGet(_analyzeEvent, false); // Don't auto-ack

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
                            channel.BasicAck(result.DeliveryTag, false); // Acknowledge successful processing
                            _logger.LogInformation("Successfully consumed and processed message {DeliveryTag} for site {SiteId}",
                                result.DeliveryTag, siteInfo.SiteId);
                        }
                        else
                        {
                            _logger.LogInformation("Received invalid SiteInfo message. Rejecting message {DeliveryTag}", result.DeliveryTag);
                            channel.BasicReject(result.DeliveryTag, false); // Reject and don't requeue
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message {DeliveryTag}. Rejecting message.", result.DeliveryTag);
                        channel.BasicReject(result.DeliveryTag, false); // Reject and don't requeue
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
        finally
        {
            ReturnChannel(channel);
        }

        return consumedEvents;
    }    private async Task<IModel> GetChannelAsync()
    {
        await _channelSemaphore.WaitAsync();

        try
        {
            // Check pool for valid channels - validate both IsOpen and !IsClosed
            while (_channelPool.TryDequeue(out var pooledChannel))
            {
                if (IsChannelValid(pooledChannel))
                {
                    return pooledChannel;
                }
<<<<<<< HEAD
                
=======

>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
                // Channel is invalid - dispose it
                try
                {
                    pooledChannel?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error disposing invalid channel: {Error}", ex.Message);
                }
            }

            // Create new channel with validation
            var connection = GetConnection();
            var channel = connection.CreateModel();
<<<<<<< HEAD
            
=======

>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
            // Validate new channel before returning
            if (!IsChannelValid(channel))
            {
                channel?.Dispose();
                throw new InvalidOperationException("Created channel is not valid");
            }
<<<<<<< HEAD
            
=======

>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
            return channel;
        }
        catch
        {
            _channelSemaphore.Release();
            throw;
        }
    }

    // Helper method to validate channel state
    private static bool IsChannelValid(IModel? channel)
    {
        return channel != null && channel.IsOpen && !channel.IsClosed;
<<<<<<< HEAD
    }    private void ReturnChannel(IModel channel)
=======
    }
    private void ReturnChannel(IModel channel)
>>>>>>> 9944afd5066f489756bf1735b46245bbc6e7d92a
    {
        try
        {
            if (IsChannelValid(channel) && _channelPool.Count < MaxChannelPoolSize)
            {
                _channelPool.Enqueue(channel);
            }
            else
            {
                // Channel is invalid or pool is full - dispose it
                try
                {
                    channel?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error disposing channel: {Error}", ex.Message);
                }
            }
        }
        finally
        {
            _channelSemaphore.Release();
        }
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
                // Dispose channel pool
                while (_channelPool.TryDequeue(out var channel))
                {
                    try
                    {
                        if (channel.IsOpen) channel.Close();
                        channel.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing pooled channel");
                    }
                }

                // Dispose main connection
                if (_connection != null)
                {
                    try
                    {
                        if (_connection.IsOpen)
                        {
                            _connection.Close();
                        }
                        _connection.Dispose();
                        _connection = null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing connection");
                    }
                }

                // Dispose semaphore
                _channelSemaphore?.Dispose();

                _logger.LogInformation("RabbitMQ connection disposed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing RabbitMQ connection");
            }
        }
    }

    /// <summary>
    /// Fast serialization method that uses System.Text.Json for performance 
    /// while maintaining format compatibility with Newtonsoft.Json
    /// </summary>
    private static string FastSerialize<T>(T obj) where T : class
    {
        try
        {
            // Try System.Text.Json first for performance (3-5x faster)
            return SystemTextJson.JsonSerializer.Serialize(obj, _fastJsonOptions);
        }
        catch
        {
            // Fallback to Newtonsoft.Json for compatibility if needed
            return JsonConvert.SerializeObject(obj);
        }
    }
}
