using AnalyzeDomains.Domain.Models.AnalyzeModels;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface ISecurityAnalyzer
    {
        Task<List<SecurityFinding>> AnalyzeSecurityAsync(string url, CancellationToken cancellationToken = default);
    }
}
