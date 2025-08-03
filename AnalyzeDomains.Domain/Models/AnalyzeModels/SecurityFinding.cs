namespace AnalyzeDomains.Domain.Models.AnalyzeModels
{
    public class SecurityFinding
    {
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public SeverityLevel Severity { get; set; }
        public string Details { get; set; } = string.Empty;
        public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    }
    public enum SeverityLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
}
