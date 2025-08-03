using AnalyzeDomains.Domain.Enums;

namespace AnalyzeDomains.Domain.Models.Events
{
    public class XmlRpcCompletedEvent : BaseCompletedEvent
    {
        public override EventType EventType => EventType.XmlRpcCompleted;
    }
}
