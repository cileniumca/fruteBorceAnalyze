using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models.Events
{
    public class WpLoginCompletedEvent : BaseCompletedEvent
    {
        public override EventType EventType => EventType.WpLoginCompleted;
    }
}
