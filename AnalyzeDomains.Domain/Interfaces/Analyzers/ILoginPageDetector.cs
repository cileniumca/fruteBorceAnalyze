using AnalyzeDomains.Domain.Models.AnalyzeModels;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface ILoginPageDetector
    {
        Task<List<WordPressLoginPage>> DetectLoginPagesAsync(string url, CancellationToken cancellationToken = default);
    }
}
