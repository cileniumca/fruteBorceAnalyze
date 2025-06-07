using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class BatchProcessorWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly IConfiguration _configuration;
        public BatchProcessorWorker(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory)
        {
            _configuration = configuration;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var maxParallelism = (int)Math.Min((GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024) / 100, Environment.ProcessorCount * 4);
            if (maxParallelism < 1) maxParallelism = 1;
            ConcurrentBag<int> publicSitesToDeactivate = new ConcurrentBag<int>();

            while (!stoppingToken.IsCancellationRequested)
            {
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
                                    var users = await userDetector.EnumerateUsersAsync(fullDomain, DetectionMode.Mixed, 10, stoppingToken);

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
                                }
                                catch (Exception ex)
                                {
                                    // Optionally log the exception
                                }
                                finally
                                {
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
