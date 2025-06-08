using AnalyzeDomains.Domain.Models;

namespace AnalyzeDomains.Domain.Interfaces.Services
{
    public interface IRabbitMQService
    {
        Task PublishBatchCompletedEventAsync(CompletedEvent eventData, CancellationToken cancellationToken = default);
    }
}
