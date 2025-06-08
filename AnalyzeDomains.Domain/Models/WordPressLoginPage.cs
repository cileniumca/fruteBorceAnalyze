using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models
{
    public class WordPressLoginPage
    {
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = "Unknown"; // WordPress Standard, WordPress Admin, Generic Login, etc.
        public string? Title { get; set; }
        public bool IsAccessible { get; set; }
        public bool MainLoginPage { get; set; }
        public bool RequiresRedirection { get; set; }
        public string? DetectionMethod { get; set; }
        public ConfidenceLevel Confidence { get; set; }
        public List<string> Features { get; set; } = new();
        public int StatusCode { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
