using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models
{
    public class WordPressUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public List<string> Roles { get; set; } = new();
        public ConfidenceLevel Confidence { get; set; }
        public string? DetectionMethod { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
