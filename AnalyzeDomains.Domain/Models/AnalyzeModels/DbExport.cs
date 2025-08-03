using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models.AnalyzeModels
{
    public class DbExport
    {
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string DetectionMethod { get; set; } = string.Empty;
        public ConfidenceLevel Confidence { get; set; }
        public long Size { get; set; }
        public string? Preview { get; set; }
        public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    }
}
