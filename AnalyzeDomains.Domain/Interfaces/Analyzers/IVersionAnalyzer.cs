using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Models.AnalyzeModels;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface IVersionAnalyzer
    {
        Task<WordPressVersion?> DetectVersionAsync(string url, DetectionMode mode, ConfidenceLevel minConfidence, CancellationToken cancellationToken = default);
    }
}
