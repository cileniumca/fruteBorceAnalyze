using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Models.AnalyzeModels;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface IUserDetector
    {
        Task<List<WordPressUser>> EnumerateUsersAsync(string url, DetectionMode mode, int maxUsers, CancellationToken cancellationToken = default);
    }
}
