using AnalyzeDomains.Domain.Models;

namespace AnalyzeDomains.Domain.Interfaces.Services
{    public interface ISocksService
    {
        Task<HttpClient> GetHttpWithSocksConnection();
        Task<HttpClient> GetHttpWithBalancedSocksConnection();
        Task<(HttpClient Client, SocksSettings? ProxyUsed)> GetHttpWithBalancedSocksConnectionAndProxyInfo();
        Task ReloadConfigurationAsync();
        Task<bool> IsConfigurationUpdatedAsync();
        Task<SocksProxyConfiguration> GetCurrentConfigurationAsync();
        Task<IEnumerable<SocksSettings>> GetHealthyProxiesAsync();
        Task DeactivateProxyAsync(SocksSettings proxy);
        Task DeactivateProxyAsync(string host, int port, string userName);
        Task<(int Healthy, int Deactivated, int Total)> GetProxyStatisticsAsync();
    }
}
