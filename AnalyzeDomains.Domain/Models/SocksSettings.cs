namespace AnalyzeDomains.Domain.Models
{
    public class SocksSettings
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public bool IsHealthy { get; set; } = true;
        public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;
        public int FailureCount { get; set; } = 0;
        public int RequestCount { get; set; } = 0;
    }

    public class SocksProxyConfiguration
    {
        public List<SocksSettings> Proxies { get; set; } = new List<SocksSettings>();
        public int HealthCheckIntervalMinutes { get; set; } = 5;
        public int MaxFailuresBeforeDisable { get; set; } = 3;
        public int ProxyTimeoutSeconds { get; set; } = 30;
    }
}
