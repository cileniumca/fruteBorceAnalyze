using AnalyzeDomains.Domain.Models.AnalyzeModels;

namespace AnalyzeDomains.Domain.Interfaces.Analyzers
{
    public interface IDbExportDetector
    {
        Task<List<DbExport>> DetectDbExportsAsync(string url, CancellationToken cancellationToken = default);
    }
}
