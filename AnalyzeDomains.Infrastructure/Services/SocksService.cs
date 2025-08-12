using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MihaZupan;
using System.Collections.Concurrent;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class SocksService : ISocksService, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly IMinIOSocksConfigurationService _minioConfigService;
        private readonly ILogger<SocksService> _logger;
        private readonly ConcurrentDictionary<string, HttpClient> _httpClientPool = new();
        private readonly SemaphoreSlim _semaphore = new(64, 64);
        private readonly Timer _configurationCheckTimer;
        private readonly Timer _healthCheckTimer;
        private readonly ConcurrentDictionary<string, DateTime> _deactivatedProxies = new();

        private SocksProxyConfiguration _currentConfiguration = new();
        private DateTime _lastConfigurationLoad = DateTime.MinValue;
        private int _currentProxyIndex = 0;
        private readonly object _indexLock = new object();
        private bool _disposed = false;

        private static readonly Random _random = Random.Shared;
        private readonly IReadOnlyList<string> UserAgents = new List<string>()
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:89.0) Gecko/20100101 Firefox/89.0",
            "Mozilla/5.0 (X11; Linux x86_64; rv:89.0) Gecko/20100101 Firefox/89.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36 Edg/91.0.864.59",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.1.1 Safari/605.1.15"
        };

        public SocksService(IConfiguration configuration, IMinIOSocksConfigurationService minioConfigService, ILogger<SocksService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _minioConfigService = minioConfigService ?? throw new ArgumentNullException(nameof(minioConfigService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Initialize configuration
            _ = Task.Run(async () => await LoadInitialConfigurationAsync());

            // Set up periodic configuration check (every 10 minutes)
            _configurationCheckTimer = new Timer(CheckConfigurationUpdate, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

            // Set up periodic health check (every 5 minutes)
            _healthCheckTimer = new Timer(PerformHealthChecks, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            _logger.LogInformation("SocksService initialized as singleton with MinIO configuration support. Deactivated proxy state will persist across requests.");
        }
        public Task<HttpClient> GetHttpWithSocksConnection()
        {
            // Fallback to the original single-proxy configuration for backward compatibility
            var socksSettings = _configuration.GetSection("SocksSettings").Get<SocksSettings>();
            if (socksSettings?.Host == null)
            {
                throw new InvalidOperationException("Socks host settings are not properly configured");
            }

            var clientKey = $"{socksSettings.Host}:{socksSettings.Port}:{socksSettings.UserName}";

            var client = _httpClientPool.GetOrAdd(clientKey, _ => CreateHttpClientWithProxy(socksSettings));
            return Task.FromResult(client);
        }

        public async Task<HttpClient> GetHttpWithBalancedSocksConnection()
        {
            var healthyProxies = await GetHealthyProxiesAsync();
            var proxiesList = healthyProxies.ToList();

            if (!proxiesList.Any())
            {
                _logger.LogWarning("No healthy proxies available, falling back to configuration-based proxy");
                return await GetHttpWithSocksConnection();
            }

            // Round-robin selection
            SocksSettings selectedProxy;
            lock (_indexLock)
            {
                selectedProxy = proxiesList[_currentProxyIndex % proxiesList.Count];
                _currentProxyIndex = (_currentProxyIndex + 1) % proxiesList.Count;
            }            // Increment request count for the selected proxy
            lock (_indexLock)
            {
                selectedProxy.RequestCount++;
            }

            var clientKey = $"{selectedProxy.Host}:{selectedProxy.Port}:{selectedProxy.UserName}";

            return _httpClientPool.GetOrAdd(clientKey, _ => CreateHttpClientWithProxy(selectedProxy));
        }        public async Task<(HttpClient Client, SocksSettings? ProxyUsed)> GetHttpWithBalancedSocksConnectionAndProxyInfo()
        {
            var healthyProxies = await GetHealthyProxiesAsync();
            var proxiesList = healthyProxies.ToList();

            _logger.LogDebug("Requesting proxy connection. Healthy proxies: {HealthyCount}, Deactivated proxies: {DeactivatedCount}", 
                proxiesList.Count, _deactivatedProxies.Count);

            if (!proxiesList.Any())
            {
                _logger.LogWarning("No healthy proxies available, falling back to configuration-based proxy");
                var fallbackClient = await GetHttpWithSocksConnection();
                // Return null for proxy info since we're using fallback
                return (fallbackClient, null);
            }

            // Round-robin selection
            SocksSettings selectedProxy;
            lock (_indexLock)
            {
                selectedProxy = proxiesList[_currentProxyIndex % proxiesList.Count];
                _currentProxyIndex = (_currentProxyIndex + 1) % proxiesList.Count;
            }

            // Increment request count for the selected proxy
            lock (_indexLock)
            {
                selectedProxy.RequestCount++;
            }

            var clientKey = $"{selectedProxy.Host}:{selectedProxy.Port}:{selectedProxy.UserName}";
            var client = _httpClientPool.GetOrAdd(clientKey, _ => CreateHttpClientWithProxy(selectedProxy));

            return (client, selectedProxy);
        }
        public async Task ReloadConfigurationAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                _logger.LogInformation("Manually reloading SOCKS configuration...");
                var newConfiguration = await _minioConfigService.LoadConfigurationAsync();

                if (newConfiguration.Proxies.Count > 0)
                {
                    var oldConfiguration = _currentConfiguration;
                    _currentConfiguration = newConfiguration;
                    _lastConfigurationLoad = DateTime.UtcNow;

                    // Apply deactivated proxy state to maintain deactivated proxies across reloads
                    ApplyDeactivatedProxyState(_currentConfiguration);

                    _logger.LogInformation("Configuration reloaded successfully. Proxy count changed from {OldCount} to {NewCount}",
                        oldConfiguration.Proxies.Count, newConfiguration.Proxies.Count);

                    // Clear old HTTP clients that are no longer needed
                    await ClearUnusedHttpClientsAsync(oldConfiguration);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload configuration");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool> IsConfigurationUpdatedAsync()
        {
            try
            {
                return await _minioConfigService.IsConfigurationUpdatedAsync(_lastConfigurationLoad);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if configuration is updated");
                return false;
            }
        }

        public async Task<SocksProxyConfiguration> GetCurrentConfigurationAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                return _currentConfiguration;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IEnumerable<SocksSettings>> GetHealthyProxiesAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                return _currentConfiguration.Proxies.Where(p => p.IsHealthy).ToList();
            }
            finally
            {
                _semaphore.Release();
            }
        }
        public async Task DeactivateProxyAsync(SocksSettings proxy)
        {
            if (proxy == null) return;

            await _semaphore.WaitAsync();
            try
            {
                var existingProxy = _currentConfiguration.Proxies
                    .FirstOrDefault(p => p.Host == proxy.Host && p.Port == proxy.Port && p.UserName == proxy.UserName);

                if (existingProxy != null)
                {
                    existingProxy.IsHealthy = false;
                    existingProxy.FailureCount = _currentConfiguration.MaxFailuresBeforeDisable;
                    existingProxy.LastHealthCheck = DateTime.UtcNow;                    // Track this proxy as permanently deactivated
                    var proxyKey = $"{proxy.Host}:{proxy.Port}:{proxy.UserName}";
                    _deactivatedProxies.TryAdd(proxyKey, DateTime.UtcNow);

                    _logger.LogWarning("Deactivated proxy {Host}:{Port} due to error. Total deactivated proxies: {DeactivatedCount}", 
                        proxy.Host, proxy.Port, _deactivatedProxies.Count);

                    // Remove the HTTP client from the pool to prevent reuse
                    var clientKey = $"{proxy.Host}:{proxy.Port}:{proxy.UserName}";
                    if (_httpClientPool.TryRemove(clientKey, out var client))
                    {
                        client?.Dispose();
                        _logger.LogDebug("Disposed HTTP client for deactivated proxy {Host}:{Port}", proxy.Host, proxy.Port);
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DeactivateProxyAsync(string host, int port, string userName)
        {
            await _semaphore.WaitAsync();
            try
            {
                var proxy = _currentConfiguration.Proxies
                    .FirstOrDefault(p => p.Host == host && p.Port == port && p.UserName == userName);

                if (proxy != null)
                {
                    await DeactivateProxyAsync(proxy);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<(int Healthy, int Deactivated, int Total)> GetProxyStatisticsAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                var totalProxies = _currentConfiguration.Proxies.Count;
                var healthyProxies = _currentConfiguration.Proxies.Count(p => p.IsHealthy);
                var deactivatedProxiesCount = _deactivatedProxies.Count;

                return (healthyProxies, deactivatedProxiesCount, totalProxies);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        private void ApplyDeactivatedProxyState(SocksProxyConfiguration configuration)
        {
            var deactivatedCount = 0;
            foreach (var proxy in configuration.Proxies)
            {
                var proxyKey = $"{proxy.Host}:{proxy.Port}:{proxy.UserName}";
                if (_deactivatedProxies.ContainsKey(proxyKey))
                {
                    proxy.IsHealthy = false;
                    proxy.FailureCount = configuration.MaxFailuresBeforeDisable;
                    proxy.LastHealthCheck = DateTime.UtcNow;
                    deactivatedCount++;

                    _logger.LogDebug("Applied deactivated state to proxy {Host}:{Port} during configuration load", proxy.Host, proxy.Port);
                }
            }
            
            if (deactivatedCount > 0)
            {
                _logger.LogInformation("Applied deactivated state to {DeactivatedCount} proxies during configuration load. Total tracked deactivated proxies: {TotalDeactivated}", 
                    deactivatedCount, _deactivatedProxies.Count);
            }
        }

        private void CleanupOldDeactivatedProxies()
        {
            // Remove proxies that have been deactivated for more than 24 hours
            var cutoffTime = DateTime.UtcNow.AddHours(-24);
            var keysToRemove = _deactivatedProxies
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_deactivatedProxies.TryRemove(key, out _))
                {
                    _logger.LogDebug("Removed old deactivated proxy entry: {Key}", key);
                }
            }

            if (keysToRemove.Any())
            {
                _logger.LogInformation("Cleaned up {Count} old deactivated proxy entries", keysToRemove.Count);
            }
        }

        private async Task LoadInitialConfigurationAsync()
        {
            try
            {
                _logger.LogInformation("Loading initial SOCKS configuration from MinIO...");
                var configuration = await _minioConfigService.LoadConfigurationAsync();

                await _semaphore.WaitAsync();
                try
                {
                    _currentConfiguration = configuration;
                    _lastConfigurationLoad = DateTime.UtcNow;

                    // Apply deactivated proxy state from previously deactivated proxies
                    ApplyDeactivatedProxyState(_currentConfiguration);
                }
                finally
                {
                    _semaphore.Release();
                }

                _logger.LogInformation("Initial configuration loaded with {ProxyCount} proxies", configuration.Proxies.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load initial configuration");
            }
        }

        private async void CheckConfigurationUpdate(object? state)
        {
            try
            {
                if (await IsConfigurationUpdatedAsync())
                {
                    _logger.LogInformation("Configuration update detected, reloading...");
                    await ReloadConfigurationAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during configuration update check");
            }
        }

        private async void PerformHealthChecks(object? state)
        {
            try
            {
                await _semaphore.WaitAsync();
                var proxiesToCheck = _currentConfiguration.Proxies.ToList();
                _semaphore.Release();

                if (!proxiesToCheck.Any())
                    return;

                _logger.LogDebug("Performing health checks on {ProxyCount} proxies", proxiesToCheck.Count);

                var healthCheckTasks = proxiesToCheck.Select(async proxy =>
                {
                    try
                    {
                        await CheckProxyHealthAsync(proxy);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Health check failed for proxy {Host}:{Port}", proxy.Host, proxy.Port);
                        proxy.FailureCount++;
                        proxy.IsHealthy = proxy.FailureCount < _currentConfiguration.MaxFailuresBeforeDisable;
                    }
                    finally
                    {
                        proxy.LastHealthCheck = DateTime.UtcNow;
                    }
                }); await Task.WhenAll(healthCheckTasks);

                var healthyCount = proxiesToCheck.Count(p => p.IsHealthy);
                _logger.LogDebug("Health check completed. {HealthyCount}/{TotalCount} proxies are healthy",
                    healthyCount, proxiesToCheck.Count);

                // Cleanup old deactivated proxy entries during health checks
                CleanupOldDeactivatedProxies();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check execution");
            }
        }

        private async Task CheckProxyHealthAsync(SocksSettings proxy)
        {
            var clientKey = $"{proxy.Host}:{proxy.Port}:{proxy.UserName}";
            var httpClient = _httpClientPool.GetOrAdd(clientKey, _ => CreateHttpClientWithProxy(proxy));

            try
            {
                // Simple health check by making a request to a reliable endpoint
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_currentConfiguration.ProxyTimeoutSeconds));
                var response = await httpClient.GetAsync("https://httpbin.org/ip", cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    proxy.FailureCount = 0;
                    proxy.IsHealthy = true;
                }
                else
                {
                    proxy.FailureCount++;
                    proxy.IsHealthy = proxy.FailureCount < _currentConfiguration.MaxFailuresBeforeDisable;
                }
            }
            catch
            {
                proxy.FailureCount++;
                proxy.IsHealthy = proxy.FailureCount < _currentConfiguration.MaxFailuresBeforeDisable;
            }
        }

        private HttpClient CreateHttpClientWithProxy(SocksSettings socksSettings)
        {
            try
            {
                var proxy = new HttpToSocks5Proxy(socksSettings.Host, socksSettings.Port,
                                                socksSettings.UserName, socksSettings.Password);

                var httpClientHandler = new HttpClientHandler()
                {
                    Proxy = proxy,
                    MaxConnectionsPerServer = 100,
                    UseCookies = false,
                    UseDefaultCredentials = false,
                    PreAuthenticate = false
                };

                var client = new HttpClient(httpClientHandler)
                {
                    Timeout = TimeSpan.FromSeconds(_currentConfiguration.ProxyTimeoutSeconds)
                };

                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents[_random.Next(UserAgents.Count)]);
                client.DefaultRequestHeaders.Connection.Add("keep-alive");
                client.DefaultRequestHeaders.ConnectionClose = false;

                _logger.LogDebug("Created HTTP client for proxy {Host}:{Port}", socksSettings.Host, socksSettings.Port);

                return client;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create HTTP client for proxy {Host}:{Port}", socksSettings.Host, socksSettings.Port);
                throw;
            }
        }
        private Task ClearUnusedHttpClientsAsync(SocksProxyConfiguration oldConfiguration)
        {
            var oldProxyKeys = oldConfiguration.Proxies
                .Select(p => $"{p.Host}:{p.Port}:{p.UserName}")
                .ToHashSet();

            var currentProxyKeys = _currentConfiguration.Proxies
                .Select(p => $"{p.Host}:{p.Port}:{p.UserName}")
                .ToHashSet();

            var keysToRemove = oldProxyKeys.Except(currentProxyKeys);

            foreach (var key in keysToRemove)
            {
                if (_httpClientPool.TryRemove(key, out var client))
                {
                    client?.Dispose();
                    _logger.LogDebug("Disposed unused HTTP client for proxy key: {Key}", key);
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _configurationCheckTimer?.Dispose();
                _healthCheckTimer?.Dispose();

                foreach (var client in _httpClientPool.Values)
                {
                    client?.Dispose();
                }
                _httpClientPool.Clear();

                _semaphore?.Dispose();
                _disposed = true;

                _logger.LogInformation("SocksService disposed");
            }
        }
    }
}
