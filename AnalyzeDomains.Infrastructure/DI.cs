using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
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
            services.AddScoped<IUserDetector, UserDetector>();            services.AddScoped<ISecurityAnalyzer, SecurityAnalyzer>();
            services.AddScoped<IPluginDetector, PluginDetector>();
            services.AddScoped<IThemeDetector, ThemeDetector>();
            services.AddScoped<IDbExportDetector, DbExportDetector>();            // WordPress Plugin Vulnerability Analyzers
            services.AddScoped<IWordPressPluginVulnerabilityAnalyzer, WordPressPluginVulnerabilityAnalyzer>();
            services.AddScoped<AdvancedWordPressExploitAnalyzer>();
            services.AddScoped<WordPressVulnerabilityResearchService>();

            services.AddSingleton<IRabbitMQService, RabbitMQService>();// Configure MinIO settings
            services.Configure<MinioSettings>(configuration.GetSection("MinioSettings"));            // Register MinIO-based SOCKS configuration service
            services.AddSingleton<IMinIOSocksConfigurationService, MinIOSocksConfigurationService>();
            services.AddSingleton<ISocksService, SocksService>();

            // Add memory cache for performance optimization
            services.AddMemoryCache(options =>
            {
                options.SizeLimit = 10000; // Limit cache size
                options.CompactionPercentage = 0.25; // Compact when 25% over limit
            });

            // Add optimized HTTP client configuration
            services.AddHttpClient("OptimizedHttpClient", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Connection.Add("keep-alive");
                client.DefaultRequestHeaders.ConnectionClose = false;
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                MaxConnectionsPerServer = 100,
                UseCookies = false,
                UseDefaultCredentials = false,
                PreAuthenticate = false
            });

            services.AddHttpClient();
            services.AddHostedService<BatchProcessorWorker>();

            return services;
        }
    }
}