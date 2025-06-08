using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class LoginPageDetector : ILoginPageDetector
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LoginPageDetector> _logger;
        private readonly List<string> _loginPaths = new()
    {
        "/wp-login.php",
        "/wp-admin/",
        "/admin/",
        "/login/",
        "/dashboard/",
        "/backend/",
        "/cms/",
        "/wp-admin/admin.php",
        "/administrator/",
        "/admin.php",
        "/login.php",
        "/user/login",
        "/auth/login",
        "/signin",
        "/sign-in"
    };
        private readonly List<string> _loginPageIndicators = new()
    {
        "wp-login",
        "login_form",
        "loginform",
        "wordpress",
        "username",
        "password",
        "log in",
        "sign in",
        "remember me",
        "lost your password",
        "forgot password",
        "dashboard",
        "admin panel",
        "administration"
    };
        public LoginPageDetector(IHttpClientFactory httpClientFactory, ILogger<LoginPageDetector> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<List<WordPressLoginPage>> DetectLoginPagesAsync(string url, CancellationToken cancellationToken = default)
        {
            var loginPages = new List<WordPressLoginPage>();

            _logger.LogDebug("Starting login page detection for {Url}", url);

            foreach (var path in _loginPaths)
            {
                var loginPage = await CheckLoginPath(url, path, cancellationToken);
                if (loginPage != null)
                {
                    loginPages.Add(loginPage);
                }
            }

            // Method 2: Search for login links in homepage
            var homepageLoginPages = await FindLoginLinksInHomepage(url, cancellationToken);
            foreach (var page in homepageLoginPages)
            {
                if (!loginPages.Any(lp => lp.Url.Equals(page.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    loginPages.Add(page);
                }
            }

            // Method 3: Check robots.txt for admin paths
            var robotsLoginPages = await FindLoginPathsInRobots(url, cancellationToken);
            foreach (var page in robotsLoginPages)
            {
                if (!loginPages.Any(lp => lp.Url.Equals(page.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    loginPages.Add(page);
                }
            }
            foreach (var loginPage in loginPages)
            {
                if (loginPage.Url.Contains("wp-login.php"))
                {
                    loginPage.MainLoginPage = true;
                }
            }
            _logger.LogInformation("Found {Count} login pages for {Url}", loginPages.Count, url);
            return loginPages;
        }
        private async Task<WordPressLoginPage?> CheckLoginPath(string baseUrl, string path, CancellationToken cancellationToken)
        {
            try
            {
                var loginUrl = $"{baseUrl.TrimEnd('/')}{path}";
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(45);
                var response = await client.GetAsync(loginUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Analyze the content to determine if it's a login page
                    var analysis = AnalyzeLoginPageContent(content, loginUrl);

                    if (analysis.IsLoginPage)
                    {
                        _logger.LogDebug("Login page found at {LoginUrl}", loginUrl);
                        return new WordPressLoginPage
                        {
                            Url = loginUrl,
                            Type = analysis.LoginType,
                            Title = analysis.Title,
                            IsAccessible = true,
                            RequiresRedirection = false,
                            DetectionMethod = "Direct Path Check",
                            Confidence = analysis.Confidence,
                            Features = analysis.Features,
                            StatusCode = (int)response.StatusCode,
                            Metadata = new Dictionary<string, object>
                            {
                                ["response_headers"] = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
                                ["content_length"] = content.Length,
                                ["path_tested"] = path
                            }
                        };
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                         response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
                {
                    // Follow redirect to see if it leads to a login page
                    var redirectLocation = response.Headers.Location?.ToString();
                    if (!string.IsNullOrEmpty(redirectLocation))
                    {
                        var redirectedPage = await CheckRedirectedLoginPage(redirectLocation, baseUrl, cancellationToken);
                        if (redirectedPage != null)
                        {
                            redirectedPage.RequiresRedirection = true;
                            redirectedPage.DetectionMethod = "Redirect Follow";
                            redirectedPage.Metadata["original_url"] = loginUrl;
                            redirectedPage.Metadata["redirect_status"] = (int)response.StatusCode;
                            return redirectedPage;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking login path {Path} for {BaseUrl}", path, baseUrl);
            }

            return null;
        }

        private async Task<WordPressLoginPage?> CheckRedirectedLoginPage(string redirectUrl, string baseUrl, CancellationToken cancellationToken)
        {
            try
            {
                // Make the redirect URL absolute if it's relative
                if (!Uri.IsWellFormedUriString(redirectUrl, UriKind.Absolute))
                {
                    var baseUri = new Uri(baseUrl);
                    redirectUrl = new Uri(baseUri, redirectUrl).ToString();
                }

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(45);
                var response = await client.GetAsync(redirectUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var analysis = AnalyzeLoginPageContent(content, redirectUrl);

                    if (analysis.IsLoginPage)
                    {
                        return new WordPressLoginPage
                        {
                            Url = redirectUrl,
                            Type = analysis.LoginType,
                            Title = analysis.Title,
                            IsAccessible = true,
                            RequiresRedirection = true,
                            DetectionMethod = "Redirect Follow",
                            Confidence = analysis.Confidence,
                            Features = analysis.Features,
                            StatusCode = (int)response.StatusCode
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking redirected login page {RedirectUrl}", redirectUrl);
            }

            return null;
        }

        private LoginPageAnalysis AnalyzeLoginPageContent(string content, string url)
        {
            var analysis = new LoginPageAnalysis();
            var lowerContent = content.ToLowerInvariant();

            // Extract title
            var titleMatch = Regex.Match(content, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                analysis.Title = titleMatch.Groups[1].Value.Trim();
            }

            // Count login page indicators
            var indicatorCount = 0;
            var features = new List<string>();

            foreach (var indicator in _loginPageIndicators)
            {
                if (lowerContent.Contains(indicator))
                {
                    indicatorCount++;
                    features.Add(indicator);
                }
            }

            // Check for specific WordPress login elements
            if (lowerContent.Contains("wp-login"))
            {
                analysis.LoginType = "WordPress Standard";
                indicatorCount += 3; // High weight for wp-login
                features.Add("WordPress Standard Login");
            }
            else if (lowerContent.Contains("dashboard") && lowerContent.Contains("admin"))
            {
                analysis.LoginType = "WordPress Admin";
                indicatorCount += 2;
                features.Add("WordPress Admin Dashboard");
            }
            else if (lowerContent.Contains("login") && (lowerContent.Contains("username") || lowerContent.Contains("email")))
            {
                analysis.LoginType = "Generic Login";
                features.Add("Generic Login Form");
            }

            // Check for form elements
            if (Regex.IsMatch(content, @"<form[^>]*login", RegexOptions.IgnoreCase))
            {
                indicatorCount += 2;
                features.Add("Login Form Present");
            }

            if (Regex.IsMatch(content, @"input[^>]*type\s*=\s*[""']password[""']", RegexOptions.IgnoreCase))
            {
                indicatorCount += 2;
                features.Add("Password Field Present");
            }

            if (Regex.IsMatch(content, @"input[^>]*name\s*=\s*[""'](user|username|email)[""']", RegexOptions.IgnoreCase))
            {
                indicatorCount += 2;
                features.Add("Username Field Present");
            }

            // Determine confidence and if it's a login page
            analysis.Features = features;

            if (indicatorCount >= 5)
            {
                analysis.IsLoginPage = true;
                analysis.Confidence = ConfidenceLevel.High;
            }
            else if (indicatorCount >= 3)
            {
                analysis.IsLoginPage = true;
                analysis.Confidence = ConfidenceLevel.Medium;
            }
            else if (indicatorCount >= 1)
            {
                analysis.IsLoginPage = true;
                analysis.Confidence = ConfidenceLevel.Low;
            }

            return analysis;
        }
        private async Task<List<WordPressLoginPage>> FindLoginLinksInHomepage(string url, CancellationToken cancellationToken)
        {
            var loginPages = new List<WordPressLoginPage>();

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(45);
                var response = await client.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Find login links in the homepage
                    var loginLinkPatterns = new[]
                    {
                    @"href\s*=\s*[""']([^""']*(?:login|admin|dashboard|wp-admin)[^""']*)[""']",
                    @"href\s*=\s*[""']([^""']*wp-login\.php[^""']*)[""']"
                };

                    foreach (var pattern in loginLinkPatterns)
                    {
                        var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                        foreach (Match match in matches)
                        {
                            var loginUrl = match.Groups[1].Value;

                            // Make absolute URL
                            if (!Uri.IsWellFormedUriString(loginUrl, UriKind.Absolute))
                            {
                                var baseUri = new Uri(url);
                                loginUrl = new Uri(baseUri, loginUrl).ToString();
                            }

                            // Verify this is actually a login page
                            var verifiedPage = await CheckLoginPath(new Uri(loginUrl).GetLeftPart(UriPartial.Authority),
                                                                  new Uri(loginUrl).PathAndQuery, cancellationToken);
                            if (verifiedPage != null)
                            {
                                verifiedPage.DetectionMethod = "Homepage Link Analysis";
                                loginPages.Add(verifiedPage);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error finding login links in homepage for {Url}", url);
            }

            return loginPages;
        }

        private async Task<List<WordPressLoginPage>> FindLoginPathsInRobots(string url, CancellationToken cancellationToken)
        {
            var loginPages = new List<WordPressLoginPage>();

            try
            {
                var robotsUrl = $"{url.TrimEnd('/')}/robots.txt";
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(45);
                var response = await client.GetAsync(robotsUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Look for disallowed admin/login paths
                    var disallowPattern = @"Disallow:\s*([^\r\n]*(?:admin|login|wp-admin|dashboard)[^\r\n]*)";
                    var matches = Regex.Matches(content, disallowPattern, RegexOptions.IgnoreCase);

                    foreach (Match match in matches)
                    {
                        var path = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(path))
                        {
                            var loginPage = await CheckLoginPath(url, path, cancellationToken);
                            if (loginPage != null)
                            {
                                loginPage.DetectionMethod = "Robots.txt Analysis";
                                loginPages.Add(loginPage);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking robots.txt for login paths at {Url}", url);
            }

            return loginPages;
        }
    }
}
