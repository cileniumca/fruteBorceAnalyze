using AnalyzeDomains.Domain.Models;

namespace AnalyzeDomains.Domain.Interfaces.Services
{
    public interface IRabbitMQService
    {
        Task PublishBatchCompletedEventAsync(CompletedEvent eventData, CancellationToken cancellationToken = default);
        Task PublishBatchCompletedEventAsync(CompletedEvent eventData, List<WordPressUser> users, CancellationToken cancellationToken = default);
    }
}
