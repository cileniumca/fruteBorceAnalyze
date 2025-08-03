using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models.Events;

public abstract class BaseCompletedEvent
{
    public required int SiteId { get; set; }
    public required string FullUrl { get; set; }
    public string? LoginPage { get; set; }
    public required string Login { get; set; }
    public abstract EventType EventType { get; }
}
