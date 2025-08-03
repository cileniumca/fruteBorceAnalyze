using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Models.AnalyzeModels;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface IThemeDetector
    {
        Task<List<Theme>> DetectThemesAsync(string url, DetectionMode mode = DetectionMode.Mixed, CancellationToken cancellationToken = default);
        Task<Theme?> DetectActiveThemeAsync(string url, CancellationToken cancellationToken = default);
    }
}
