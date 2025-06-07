using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Infrastructure.Analyzers;
using AnalyzeDomains.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyzeDomains.Infrastructure
{
    public static class DI
    {
        public static IServiceCollection AddImportDomainsInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IDatabaseService, Services.DatabaseService>();
            services.AddScoped<ILoginPageDetector, LoginPageDetector>();
            services.AddScoped<IMainDomainAnalyzer, MainDomainAnalyzer>();
            services.AddScoped<IVersionAnalyzer, VersionAnalyzer>();
            services.AddScoped<IUserDetector, UserDetector>();
            services.AddHttpClient();
            services.AddHostedService<BatchProcessorWorker>();

            return services;
        }
    }
}