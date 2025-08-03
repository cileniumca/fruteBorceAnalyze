using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models.AnalyzeModels
{
    public class Plugin
    {
        public string Name { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string DetectionMethod { get; set; } = string.Empty;
        public ConfidenceLevel Confidence { get; set; }
        public bool IsActive { get; set; }
        public string? Path { get; set; }
        public List<string> Files { get; set; } = new();
        public List<SecurityFinding> SecurityIssues { get; set; } = new();
    }
}
