using AnalyzeDomains.Domain.Interfaces.Analyzers;
using Microsoft.Extensions.Logging;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class MainDomainAnalyzer : IMainDomainAnalyzer
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MainDomainAnalyzer> _logger;

        public MainDomainAnalyzer(IHttpClientFactory httpClientFactory, ILogger<MainDomainAnalyzer> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<string> MainPageAnalyzeAsync(string url, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting main page analysis for URL: {Url}", url);

            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);

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
                    catch
                    {
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
