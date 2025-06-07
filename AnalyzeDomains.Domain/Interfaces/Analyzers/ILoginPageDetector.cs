using AnalyzeDomains.Domain.Models;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface ILoginPageDetector
    {
        Task<List<WordPressLoginPage>> DetectLoginPagesAsync(string url, CancellationToken cancellationToken = default);
    }
}
