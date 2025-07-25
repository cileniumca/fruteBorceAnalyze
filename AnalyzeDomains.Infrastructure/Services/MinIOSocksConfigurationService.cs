using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Newtonsoft.Json;
using System.Text;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class MinIOSocksConfigurationService : IMinIOSocksConfigurationService, IDisposable
    {
        private readonly IMinioClient _minioClient;
        private readonly ILogger<MinIOSocksConfigurationService> _logger;
        private readonly MinioSettings _minioSettings;
        private bool _disposed = false;

        public MinIOSocksConfigurationService(IOptions<MinioSettings> minioOptions, ILogger<MinIOSocksConfigurationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _minioSettings = minioOptions?.Value ?? throw new ArgumentNullException(nameof(minioOptions));

            _minioClient = new MinioClient()
                .WithEndpoint(_minioSettings.Endpoint)
                .WithCredentials(_minioSettings.AccessKey, _minioSettings.SecretKey)
                .WithSSL(_minioSettings.UseSSL)
                .Build();

            _logger.LogInformation("MinIO SOCKS Configuration Service initialized with endpoint: {Endpoint}, bucket: {Bucket}, config file: {ConfigFile}", 
                _minioSettings.Endpoint, _minioSettings.BucketName, _minioSettings.SocksConfigFileName);
        }

        public async Task<SocksProxyConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Loading SOCKS configuration from MinIO...");

                var getObjectArgs = new GetObjectArgs()
                    .WithBucket(_minioSettings.BucketName)
                    .WithObject(_minioSettings.SocksConfigFileName);

                using var memoryStream = new MemoryStream();
                await _minioClient.GetObjectAsync(getObjectArgs.WithCallbackStream(stream =>
                {
                    stream.CopyTo(memoryStream);
                }), cancellationToken);

                memoryStream.Position = 0;
                var json = Encoding.UTF8.GetString(memoryStream.ToArray());
                
                var configuration = JsonConvert.DeserializeObject<SocksProxyConfiguration>(json);
                
                if (configuration?.Proxies?.Count > 0)
                {
                    _logger.LogInformation("Successfully loaded SOCKS configuration with {ProxyCount} proxies", configuration.Proxies.Count);
                    
                    // Initialize proxy states
                    foreach (var proxy in configuration.Proxies)
                    {
                        proxy.IsHealthy = true;
                        proxy.LastHealthCheck = DateTime.UtcNow;
                        proxy.FailureCount = 0;
                        proxy.RequestCount = 0;
                    }
                    
                    return configuration;
                }
                else
                {
                    _logger.LogWarning("Configuration file contains no valid proxies");
                    return new SocksProxyConfiguration();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load SOCKS configuration from MinIO");
                
                // Return a default configuration in case of failure
                return CreateDefaultConfiguration();
            }
        }

        public async Task<DateTime?> GetConfigurationLastModifiedAsync(CancellationToken cancellationToken = default)
        {
            try
            {                var statObjectArgs = new StatObjectArgs()
                    .WithBucket(_minioSettings.BucketName)
                    .WithObject(_minioSettings.SocksConfigFileName);

                var objectStat = await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
                return objectStat.LastModified;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get configuration last modified time from MinIO");
                return null;
            }
        }

        public async Task<bool> IsConfigurationUpdatedAsync(DateTime lastLoadTime, CancellationToken cancellationToken = default)
        {
            try
            {
                var lastModified = await GetConfigurationLastModifiedAsync(cancellationToken);
                return lastModified.HasValue && lastModified.Value > lastLoadTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if configuration is updated");
                return false;
            }
        }

        private static SocksProxyConfiguration CreateDefaultConfiguration()
        {
            return new SocksProxyConfiguration
            {
                Proxies = new List<SocksSettings>(),
                HealthCheckIntervalMinutes = 5,
                MaxFailuresBeforeDisable = 3,
                ProxyTimeoutSeconds = 30
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _minioClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
