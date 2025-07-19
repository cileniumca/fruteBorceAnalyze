using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class BatchProcessorWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IRabbitMQService _rabbitMQService;
        private readonly IConfiguration _configuration;

        public BatchProcessorWorker(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory, IRabbitMQService rabbitMQService)
        {
            _configuration = configuration;
            _serviceScopeFactory = serviceScopeFactory;
            _rabbitMQService = rabbitMQService;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var maxParallelism = 64;
            if (maxParallelism < 1)
                maxParallelism = 1;

            var batchId = 1;

            while (!stoppingToken.IsCancellationRequested)
            {
                var batchStartTime = DateTime.UtcNow;
                var publicSitesToDeactivate = new ConcurrentBag<int>();
                var successfulAnalyses = 0;
                var failedAnalyses = 0;
                List<SiteInfo> domainsList = new List<SiteInfo>();

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var dataBaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                    var loginPageDetector = scope.ServiceProvider.GetRequiredService<ILoginPageDetector>();
                    var versionAnalyzer = scope.ServiceProvider.GetRequiredService<IVersionAnalyzer>();
                    var userDetector = scope.ServiceProvider.GetRequiredService<IUserDetector>();
                    var mainDomainAnalyzer = scope.ServiceProvider.GetRequiredService<IMainDomainAnalyzer>();

                    // Consume events from the queue instead of reading from database
                    var domainsToValidate = await _rabbitMQService.ConsumeAnalyzeEventsAsync(64, stoppingToken);
                    domainsList = domainsToValidate.ToList();

                    if (domainsList.Count > 0)
                    {
                        var semaphore = new SemaphoreSlim(maxParallelism);
                        var tasks = new List<Task>();

                        foreach (var domain in domainsList)
                        {
                            tasks.Add(Task.Run(async () => {
                                await semaphore.WaitAsync(stoppingToken); // Wait inside task
                                try {
                                    var isSuccess = false;
                                    try
                                    {
                                        var fullDomain = await mainDomainAnalyzer.MainPageAnalyzeAsync(domain.Domain, stoppingToken);
                                        if (string.IsNullOrEmpty(fullDomain))
                                        {
                                            publicSitesToDeactivate.Add(domain.SiteId);
                                            return;
                                        }

                                        var loginPage = await loginPageDetector.DetectLoginPagesAsync(fullDomain, stoppingToken);
                                        //if (loginPage.Count == 0)
                                        //{
                                        //    publicSitesToDeactivate.Add(domain.SiteId);
                                        //    return;
                                        //}

                                        var versionInfo = await versionAnalyzer.DetectVersionAsync(fullDomain, DetectionMode.Mixed, ConfidenceLevel.Medium, stoppingToken);
                                        var users = await userDetector.EnumerateUsersAsync(fullDomain, DetectionMode.Mixed, 20, stoppingToken);

                                        if (users.Count == 0)
                                        {
                                            publicSitesToDeactivate.Add(domain.SiteId);
                                            return;
                                        }                                    // Try to add site and users to database, but continue with event publishing regardless
                                        try
                                        {
                                            await dataBaseService.AddSiteWithUsers(
                                                domain,
                                                loginPage,
                                                versionInfo ?? new Domain.Models.WordPressVersion(),
                                                users,
                                                fullDomain,
                                                stoppingToken
                                            );
                                        }
                                        catch (Exception dbEx)
                                        {
                                            // Log database error but continue with event publishing
                                            // This could happen if site/users already exist in database
                                            Console.WriteLine($"Database insertion failed for domain {fullDomain}: {dbEx.Message}");
                                        }                                        // Publish events regardless of database operation success
                                        var mainLoginPageUrl = loginPage.Where(x => x.MainLoginPage == true).Select(xx => xx.Url).FirstOrDefault() ?? string.Empty;
                                        var batchEvents = users.Select(user => new CompletedEvent
                                        {
                                            Login = user.Username,
                                            FullUrl = fullDomain,
                                            LoginPage = mainLoginPageUrl,
                                            SiteId = domain.SiteId
                                        }).ToList();
                                        
                                        await _rabbitMQService.PublishBatchCompletedEventsAsync(batchEvents, users, stoppingToken);

                                        isSuccess = true;
                                    }
                                    catch (Exception)
                                    {
                                        // Log error if needed
                                    }
                                    finally
                                    {
                                        if (isSuccess)
                                            Interlocked.Increment(ref successfulAnalyses);
                                        else
                                            Interlocked.Increment(ref failedAnalyses);
                                    }
                                }
                                finally {
                                    semaphore.Release();
                                }
                            }, stoppingToken));
                        }
                        await Task.WhenAll(tasks);
                    }
                    else
                    {
                        // No events available, wait before checking again
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                }

                // Log batch processing results
                var batchDuration = DateTime.UtcNow - batchStartTime;
                if (domainsList.Count > 0)
                {
                    Console.WriteLine($"Batch {batchId} completed: {successfulAnalyses} successful, {failedAnalyses} failed. Duration: {batchDuration:mm\\:ss}");
                }

                batchId++;
            }
        }
    }
}


// repush events
//using AnalyzeDomains.Domain.Enums;
//using AnalyzeDomains.Domain.Interfaces.Analyzers;
//using AnalyzeDomains.Domain.Interfaces.Services;
//using AnalyzeDomains.Domain.Models;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using System.Collections.Concurrent;

//namespace AnalyzeDomains.Infrastructure.Services
//{
//    public class BatchProcessorWorker : BackgroundService
//    {
//        private readonly IServiceScopeFactory _serviceScopeFactory;
//        private readonly IRabbitMQService _rabbitMQService;
//        private readonly IConfiguration _configuration;

//        public BatchProcessorWorker(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory, IRabbitMQService rabbitMQService)
//        {
//            _configuration = configuration;
//            _serviceScopeFactory = serviceScopeFactory;
//            _rabbitMQService = rabbitMQService;
//        }
//        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//        {
//            var maxParallelism = Environment.ProcessorCount * 50;
//            if (maxParallelism < 1)
//                maxParallelism = 1;

//            var batchId = 1;

//            while (!stoppingToken.IsCancellationRequested)
//            {
//                var batchStartTime = DateTime.UtcNow;
//                var publicSitesToDeactivate = new ConcurrentBag<int>();
//                var successfulAnalyses = 0;
//                var failedAnalyses = 0;

//                using (var scope = _serviceScopeFactory.CreateScope())
//                {
//                    var dataBaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
//                    var loginPageDetector = scope.ServiceProvider.GetRequiredService<ILoginPageDetector>();
//                    // var domainsToValidate = await dataBaseService.ReadAllDomainsAsync(25000, stoppingToken);

//                    var versionAnalyzer = scope.ServiceProvider.GetRequiredService<IVersionAnalyzer>();
//                    var userDetector = scope.ServiceProvider.GetRequiredService<IUserDetector>();
//                    var mainDomainAnalyzer = scope.ServiceProvider.GetRequiredService<IMainDomainAnalyzer>();

//                    var getUserInfoForQueue = await dataBaseService.ReadUserInfoForEvents(stoppingToken);
//                    getUserInfoForQueue = getUserInfoForQueue.OrderBy(x => x.SiteId).ToList();
//                    // Split events into chunks
//                    const int chunkSize = 128;
//                    var eventChunks = getUserInfoForQueue
//                        .Select((evt, idx) => new { evt, idx })
//                        .GroupBy(x => x.idx / chunkSize)
//                        .Select(g => g.Select(x => x.evt).ToList())
//                        .ToList();

//                    var semaphore = new SemaphoreSlim(50);
//                    var tasks = new List<Task>();

//                    foreach (var chunk in eventChunks)
//                    {
//                        await semaphore.WaitAsync(stoppingToken);
//                        tasks.Add(Task.Run(async () =>
//                        {
//                            try
//                            {
//                                foreach (var userEvent in chunk)
//                                {
//                                    await _rabbitMQService.PublishBatchCompletedEventAsync(userEvent, stoppingToken);
//                                }
//                            }
//                            finally
//                            {
//                                semaphore.Release();
//                            }
//                        }, stoppingToken));
//                    }

//                    await Task.WhenAll(tasks);
//                    //if (domainsToValidate.Count > 0)
//                    //{
//                    //    ... (rest of your commented code)
//                    //}
//                    //else
//                    //{
//                    //    await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
//                    //}
//                }
//            }
//        }
//    }
//}




//
//repush event with chunks
//using AnalyzeDomains.Domain.Interfaces.Analyzers;
//using AnalyzeDomains.Domain.Interfaces.Services;
//using AnalyzeDomains.Domain.Models;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using System.Collections.Concurrent;

//namespace AnalyzeDomains.Infrastructure.Services
//{
//    public class BatchProcessorWorker : BackgroundService
//    {
//        private readonly IServiceScopeFactory _serviceScopeFactory;
//        private readonly IRabbitMQService _rabbitMQService;
//        private readonly IConfiguration _configuration;
//        private readonly ILogger<BatchProcessorWorker> _logger;

//        public BatchProcessorWorker(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory, IRabbitMQService rabbitMQService, ILogger<BatchProcessorWorker> logger)
//        {
//            _configuration = configuration;
//            _serviceScopeFactory = serviceScopeFactory;
//            _rabbitMQService = rabbitMQService;
//            _logger = logger;
//        }
//        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//        {
//            const int batchSize = 25000;

//            while (!stoppingToken.IsCancellationRequested)
//            {
//                var batchStartTime = DateTime.UtcNow;
//                var successfulAnalyses = 0;
//                var hasMoreDomains = true;

//                while (hasMoreDomains && !stoppingToken.IsCancellationRequested)
//                {
//                    using (var scope = _serviceScopeFactory.CreateScope())
//                    {
//                        var dataBaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
//                        var domainsToValidate = await dataBaseService.ReadAllDomainsAsync(batchSize, stoppingToken);

//                        if (domainsToValidate.Count > 0)
//                        {
//                            // Create analyze events in batches of 1000
//                            const int eventBatchSize = 1000;
//                            var analyzeEvents = domainsToValidate.Select(domain => new AnalyzeEvent
//                            {
//                                SiteId = domain.SiteId,
//                                Domain = domain.Domain
//                            }).ToList();

//                            _logger.LogInformation("Processing {TotalDomains} domains, sending events in batches of {BatchSize}",
//                                analyzeEvents.Count, eventBatchSize);

//                            // Send events in batches of 1000
//                            var batchCount = 0;
//                            for (int i = 0; i < analyzeEvents.Count; i += eventBatchSize)
//                            {
//                                var batch = analyzeEvents.Skip(i).Take(eventBatchSize).ToList();
//                                await _rabbitMQService.PublishAnalyzeEventsBatchAsync(batch, stoppingToken);
//                                batchCount++; _logger.LogInformation("Published batch {BatchNumber}/{TotalBatches} with {EventCount} analyze events",
//                                    batchCount, (int)Math.Ceiling((double)analyzeEvents.Count / eventBatchSize), batch.Count);
//                            }

//                            successfulAnalyses += analyzeEvents.Count; // All events were sent successfully

//                            // Check if we got less than the batch size, indicating no more domains
//                            hasMoreDomains = domainsToValidate.Count == batchSize;
//                        }
//                        else
//                        {
//                            hasMoreDomains = false;
//                        }
//                    }
//                }

//                _logger.LogInformation("Batch processing cycle completed. Successfully sent {SuccessfulCount} events. Time elapsed: {ElapsedTime:mm\\:ss}",
//                    successfulAnalyses, DateTime.UtcNow - batchStartTime);

//                // If no more domains to process, wait before checking again
//                if (!hasMoreDomains)
//                {
//                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
//                }
//            }
//        }
//    }
//}
