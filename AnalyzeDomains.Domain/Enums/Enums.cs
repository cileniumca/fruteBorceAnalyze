namespace AnalyzeDomains.Domain.Enums
{
    public enum DetectionMode
    {
        Mixed,
        Passive,
        Aggressive
    }
    public enum ConfidenceLevel
    {
        Low = 0,
        Medium = 50,
        High = 80,
        Certain = 100
    }

    public enum EventType
    {
        XmlRpcCompleted,
        WpLoginCompleted
    }
}
