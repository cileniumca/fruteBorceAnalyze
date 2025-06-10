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
            var maxParallelism = Environment.ProcessorCount*1280;
            if (maxParallelism < 1) 
                maxParallelism = 1;

            var batchId = 1;

            while (!stoppingToken.IsCancellationRequested)
            {
                var batchStartTime = DateTime.UtcNow;
                var publicSitesToDeactivate = new ConcurrentBag<int>();
                var successfulAnalyses = 0;
                var failedAnalyses = 0;

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var dataBaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                    var loginPageDetector = scope.ServiceProvider.GetRequiredService<ILoginPageDetector>();
                    var domainsToValidate = await dataBaseService.ReadAllDomainsAsync(25000, stoppingToken);
                    var versionAnalyzer = scope.ServiceProvider.GetRequiredService<IVersionAnalyzer>();
                    var userDetector = scope.ServiceProvider.GetRequiredService<IUserDetector>();
                    var mainDomainAnalyzer = scope.ServiceProvider.GetRequiredService<IMainDomainAnalyzer>();

                    if (domainsToValidate.Count > 0)
                    {
                        var semaphore = new SemaphoreSlim(maxParallelism);
                        var tasks = new List<Task>();

                        foreach (var domain in domainsToValidate.OrderBy(x => x.Domain))
                        {
                            await semaphore.WaitAsync(stoppingToken);

                            tasks.Add(Task.Run(async () =>
                            {
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
                                    if (loginPage.Count == 0)
                                    {
                                        publicSitesToDeactivate.Add(domain.SiteId);
                                        return;
                                    }

                                    var versionInfo = await versionAnalyzer.DetectVersionAsync(fullDomain, DetectionMode.Mixed, ConfidenceLevel.Medium, stoppingToken);
                                    var users = await userDetector.EnumerateUsersAsync(fullDomain, DetectionMode.Mixed, 20, stoppingToken);

                                    if (users.Count == 0)
                                    {
                                        publicSitesToDeactivate.Add(domain.SiteId);
                                        return;
                                    }

                                    await dataBaseService.AddSiteWithUsers(
                                        domain,
                                        loginPage,
                                        versionInfo ?? new Domain.Models.WordPressVersion(),
                                        users,
                                        fullDomain,
                                        stoppingToken
                                    );



                                    foreach (var user in users)
                                    {
                                        var batchEvent = new CompletedEvent
                                        {
                                            Login = user.Username,
                                            FullUrl = fullDomain,
                                            LoginPage = loginPage.Where(x => x.MainLoginPage == true).Select(xx => xx.Url).FirstOrDefault(),
                                            SiteId = domain.SiteId
                                        };
                                        await _rabbitMQService.PublishBatchCompletedEventAsync(batchEvent, stoppingToken);

                                    }



                                }
                                catch (Exception)
                                {
                                }
                                finally
                                {
                                    if (isSuccess)
                                        Interlocked.Increment(ref successfulAnalyses);
                                    else
                                        Interlocked.Increment(ref failedAnalyses);

                                    semaphore.Release();
                                }
                            }, stoppingToken));
                        }
                        await Task.WhenAll(tasks);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
                    }
                }
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
//            var maxParallelism = Environment.ProcessorCount * 1280;
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

//                    // Split events into chunks
//                    const int chunkSize = 128;
//                    var eventChunks = getUserInfoForQueue
//                        .Select((evt, idx) => new { evt, idx })
//                        .GroupBy(x => x.idx / chunkSize)
//                        .Select(g => g.Select(x => x.evt).ToList())
//                        .ToList();

//                    var semaphore = new SemaphoreSlim(128);
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
