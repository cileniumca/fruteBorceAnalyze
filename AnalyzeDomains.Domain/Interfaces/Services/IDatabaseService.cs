using AnalyzeDomains.Domain.Models;

namespace AnalyzeDomains.Domain.Interfaces.Services
{
    public interface IDatabaseService
    {
        Task AddSiteWithUsers(SiteInfo siteInfo, List<WordPressLoginPage> wordPressLoginPages, WordPressVersion version, List<WordPressUser> wordPressUser, string fullDomain, CancellationToken cancellationToken);
        Task<List<SiteInfo>> ReadAllDomainsAsync(int batchSize = 25000, CancellationToken cancellationToken = default);
    }
}
