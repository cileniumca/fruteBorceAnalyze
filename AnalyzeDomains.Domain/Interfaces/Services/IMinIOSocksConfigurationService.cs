using AnalyzeDomains.Domain.Models;

namespace AnalyzeDomains.Domain.Interfaces.Services
{
    public interface IMinIOSocksConfigurationService
    {
        Task<SocksProxyConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken = default);
        Task<DateTime?> GetConfigurationLastModifiedAsync(CancellationToken cancellationToken = default);
        Task<bool> IsConfigurationUpdatedAsync(DateTime lastLoadTime, CancellationToken cancellationToken = default);
    }
}
