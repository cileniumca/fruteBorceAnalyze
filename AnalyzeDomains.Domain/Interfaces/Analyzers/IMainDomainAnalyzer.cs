namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface IMainDomainAnalyzer
    {
        Task<string> MainPageAnalyzeAsync(string url, CancellationToken cancellationToken = default);
    }
}
