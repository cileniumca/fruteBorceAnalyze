using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class MainDomainAnalyzer : IMainDomainAnalyzer
    {
        private readonly ISocksService _socksService;
        private readonly ILogger<MainDomainAnalyzer> _logger;

        public MainDomainAnalyzer(ISocksService socksService, ILogger<MainDomainAnalyzer> logger)
        {
            _socksService = socksService;
            _logger = logger;
        }

        public async Task<string> MainPageAnalyzeAsync(string url, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting main page analysis for URL: {Url}", url);
            try
            {
                // Use balanced SOCKS connection for better load distribution
                var client = await _socksService.GetHttpWithBalancedSocksConnection();

                foreach (var scheme in new[] { "https", "http" })
                {
                    var fullUrl = EnsureScheme(url, scheme);
                    try
                    {
                        var response = await client.GetAsync(fullUrl, cancellationToken);

                        if (response.IsSuccessStatusCode || IsRedirectResponse(response))
                        {
                            var redirectUrl = GetRedirectUrl(response, fullUrl);
                            return redirectUrl ?? fullUrl;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.ToLower().Contains("proxy"))
                        {
                            _logger.LogError("PROXY ERROR!!!!");
                            Thread.Sleep(TimeSpan.FromHours(20));
                        }
                        continue;
                    }

                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during main page analysis for URL: {Url}", url);
                throw;
            }
        }

        private static string EnsureScheme(string url, string scheme) =>
            $"{scheme}://{url}";

        private static bool IsRedirectResponse(HttpResponseMessage response) =>
            (int)response.StatusCode is >= 300 and < 400;

        private static string? GetRedirectUrl(HttpResponseMessage response, string originalUrl)
        {
            if (response.Headers.Location == null) return null;

            var location = response.Headers.Location;
            var redirectUri = location.IsAbsoluteUri
                ? location
                : new Uri(new Uri(originalUrl), location);

            return redirectUri.ToString();
        }
    }
}
