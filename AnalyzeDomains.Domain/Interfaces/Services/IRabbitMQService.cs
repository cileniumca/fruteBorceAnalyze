using AnalyzeDomains.Domain.Models;
using AnalyzeDomains.Domain.Models.AnalyzeModels;
using AnalyzeDomains.Domain.Models.Events;

namespace AnalyzeDomains.Domain.Interfaces.Services
{    public interface IRabbitMQService
    {
        Task PublishAnalyzeEventAsync(AnalyzeEvent eventData, CancellationToken cancellationToken = default);
        Task PublishAnalyzeEventsBatchAsync(IEnumerable<AnalyzeEvent> events, CancellationToken cancellationToken = default);
        Task PublishCompletedEventsBatchAsync(IEnumerable<CompletedEvent> events, CancellationToken cancellationToken = default);
        Task PublishBatchCompletedEventAsync(CompletedEvent eventData, CancellationToken cancellationToken = default);
        Task PublishBatchCompletedEventAsync(CompletedEvent eventData, List<WordPressUser> users, CancellationToken cancellationToken = default);
        Task PublishBatchCompletedEventsAsync(List<CompletedEvent> events, List<WordPressUser> users, CancellationToken cancellationToken = default);
        Task<IEnumerable<SiteInfo>> ConsumeAnalyzeEventsAsync(int maxMessages, CancellationToken cancellationToken = default);
        bool TestConnection();
    }
}
