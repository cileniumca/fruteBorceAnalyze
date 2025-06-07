using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models
{
    public class LoginPageAnalysis
    {
        public bool IsLoginPage { get; set; }
        public string LoginType { get; set; } = "Unknown";
        public string? Title { get; set; }
        public ConfidenceLevel Confidence { get; set; }
        public List<string> Features { get; set; } = new();
    }
}
