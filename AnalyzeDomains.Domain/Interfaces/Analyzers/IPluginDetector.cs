using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Models;
using AnalyzeDomains.Domain.Models.AnalyzeModels;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface IPluginDetector
    {
        Task<List<Plugin>> DetectPluginsAsync(string url, DetectionMode mode = DetectionMode.Mixed, CancellationToken cancellationToken = default);
    }
}
