using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models
{
    public class WordPressVersion
    {
        public string? Version { get; set; }
        public ConfidenceLevel Confidence { get; set; }
        public string? DetectionMethod { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}

