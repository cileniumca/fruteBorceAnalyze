using AnalyzeDomains.Domain.Models;
using AnalyzeDomains.Domain.Models.AnalyzeModels;
using AnalyzeDomains.Domain.Models.Events;

namespace AnalyzeDomains.Domain.Interfaces.Services
{    public interface IDatabaseService
    {
        Task<int> AddSiteWithUsers(SiteInfo siteInfo, List<WordPressLoginPage> wordPressLoginPages, WordPressVersion version, List<WordPressUser> wordPressUser, string fullDomain, CancellationToken cancellationToken);
        Task<List<SiteInfo>> ReadAllDomainsAsync(int batchSize = 25000, CancellationToken cancellationToken = default);
        Task<List<CompletedEvent>> ReadUserInfoForEvents(CancellationToken cancellationToken);
        Task SiteWasValidated(SiteInfo siteInfo, CancellationToken cancellationToken);        // New insert methods for database tables        Task InsertSiteDumpInfoAsync(int siteId, List<DbExport> dbExports, CancellationToken cancellationToken = default);
        Task InsertSiteFilesInfoAsync(int siteId, List<SecurityFinding> securityFindings, CancellationToken cancellationToken = default);
        Task InsertSitePluginsAsync(int siteId, List<Plugin> plugins, CancellationToken cancellationToken = default);
        Task InsertSiteThemesAsync(int siteId, List<Theme> themes, CancellationToken cancellationToken = default);
        Task InsertSiteDumpInfoAsync(int siteId, List<DbExport> dbExports, CancellationToken cancellationToken = default);
    }
}
