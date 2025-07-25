using AnalyzeDomains.Domain.Models;

namespace AnalyzeDomains.Domain.Interfaces.Services
{
    public interface ISocksService
    {
        Task<HttpClient> GetHttpWithSocksConnection();
        Task<HttpClient> GetHttpWithBalancedSocksConnection();
        Task ReloadConfigurationAsync();
        Task<bool> IsConfigurationUpdatedAsync();
        Task<SocksProxyConfiguration> GetCurrentConfigurationAsync();
        Task<IEnumerable<SocksSettings>> GetHealthyProxiesAsync();
    }
}
