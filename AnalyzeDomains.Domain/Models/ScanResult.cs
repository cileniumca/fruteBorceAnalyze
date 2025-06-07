namespace AnalyzeDomains.Domain.Models
{
    public class ScanResult
    {
        public string TargetUrl { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; } = DateTime.UtcNow;
        public WordPressVersion? Version { get; set; }
        public List<WordPressUser> Users { get; set; } = new();
        public List<WordPressLoginPage> LoginPages { get; set; } = new(); // Add login pages to scan results
        public List<string> Vulnerabilities { get; set; } = new();
        public Dictionary<string, object> TechnicalDetails { get; set; } = new();
        public TimeSpan ScanDuration { get; set; }
        public bool IsWordPress { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
