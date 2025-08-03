using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models.AnalyzeModels;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class SecurityAnalyzer : ISecurityAnalyzer
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SecurityAnalyzer> _logger;
        private readonly ISocksService _socksService;

        private readonly Dictionary<string, string> _securityPaths = new()
        {
            ["/wp-config.php"] = "Configuration file exposure",
            ["/wp-config.php.bak"] = "Configuration backup file",
            ["/wp-config.txt"] = "Configuration text file",
            ["/wp-config.php.old"] = "Old configuration file",
            ["/wp-config.php.save"] = "Saved configuration file",            
            ["/wp-admin/install.php"] = "Installation file",
            ["/wp-content/debug.log"] = "Debug log file",
            ["/wp-includes/version.php"] = "Version file",
            ["/wp-cron.php"] = "Cron file",
            ["/xmlrpc.php"] = "XML-RPC interface",
            ["/wp-signup.php"] = "Multisite signup",
            ["/.htaccess"] = "Apache configuration",
            ["/wp-config.php~"] = "Configuration backup",
            ["/wp-admin/setup-config.php"] = "Setup configuration"
        };

        private readonly List<string> _directoryListingPaths = new()
    {
        "/wp-content/",
        "/wp-content/uploads/",
        "/wp-content/plugins/",
        "/wp-content/themes/",
        "/wp-includes/",
        "/wp-admin/"
    };

        public SecurityAnalyzer(IHttpClientFactory httpClientFactory,
                               ILogger<SecurityAnalyzer> logger,
                               ISocksService socksService
                               )
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _socksService = socksService;

        }

        public async Task<List<SecurityFinding>> AnalyzeSecurityAsync(string url, CancellationToken cancellationToken = default)
        {
            var findings = new List<SecurityFinding>();
            var client = await _socksService.GetHttpWithBalancedSocksConnection();

            try
            {
                // Check for exposed files
                var fileFindings = await CheckExposedFilesAsync(client, url, cancellationToken);
                findings.AddRange(fileFindings);

                // Check for directory listing
                var directoryFindings = await CheckDirectoryListingAsync(client, url, cancellationToken);
                findings.AddRange(directoryFindings);

                // Check for information disclosure
                var infoFindings = await CheckInformationDisclosureAsync(client, url, cancellationToken);
                findings.AddRange(infoFindings);

                // Check XML-RPC
                var xmlrpcFindings = await CheckXmlRpcAsync(client, url, cancellationToken);
                findings.AddRange(xmlrpcFindings);

                // Check registration
                var registrationFindings = await CheckRegistrationAsync(client, url, cancellationToken);
                findings.AddRange(registrationFindings);

                // Check for multisite
                var multisiteFindings = await CheckMultisiteAsync(client, url, cancellationToken);
                findings.AddRange(multisiteFindings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing security for {Url}", url);
            }

            return findings;
        }

        private async Task<List<SecurityFinding>> CheckExposedFilesAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var findings = new List<SecurityFinding>();

            foreach (var path in _securityPaths)
            {
                try
                {
                    var fileUrl = $"{url.TrimEnd('/')}{path.Key}";
                    var response = await client.GetAsync(fileUrl, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken);

                        // Additional validation for some files
                        var isValidExposure = path.Key switch
                        {
                            "/wp-config.php" => content.Contains("DB_NAME") || content.Contains("DB_USER"),
                            "/readme.html" => content.Contains("WordPress") && content.Contains("Version"),
                            "/wp-includes/version.php" => content.Contains("$wp_version"),
                            _ => !string.IsNullOrEmpty(content)
                        };

                        if (isValidExposure)
                        {
                            var severity = path.Key.Contains("wp-config") ? SeverityLevel.High :
                                         path.Key.Contains("debug.log") ? SeverityLevel.Medium : SeverityLevel.Low;

                            findings.Add(new SecurityFinding
                            {
                                Type = "File Exposure",
                                Description = path.Value,
                                Url = fileUrl,
                                Severity = severity,
                                Details = $"Accessible file: {path.Key}"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to check {Path} for {Url}", path.Key, url);
                }
            }

            return findings;
        }

        private async Task<List<SecurityFinding>> CheckDirectoryListingAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var findings = new List<SecurityFinding>();

            foreach (var path in _directoryListingPaths)
            {
                try
                {
                    var dirUrl = $"{url.TrimEnd('/')}{path}";
                    var response = await client.GetAsync(dirUrl, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken);

                        // Check for directory listing indicators
                        if (content.Contains("Index of") ||
                            content.Contains("<title>Index of") ||
                            content.Contains("Directory Listing") ||
                            (content.Contains("<table>") && content.Contains("Parent Directory")))
                        {
                            findings.Add(new SecurityFinding
                            {
                                Type = "Directory Listing",
                                Description = "Directory listing enabled",
                                Url = dirUrl,
                                Severity = SeverityLevel.Medium,
                                Details = $"Directory listing accessible at: {path}"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to check directory listing for {Path} on {Url}", path, url);
                }
            }

            return findings;
        }

        private async Task<List<SecurityFinding>> CheckInformationDisclosureAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var findings = new List<SecurityFinding>();

            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) return findings;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                // Check for WordPress version disclosure in generator meta tag
                var generatorMatch = Regex.Match(content, @"<meta\s+name=['""]generator['""].*?content=['""]WordPress\s+([\d.]+)['""]", RegexOptions.IgnoreCase);
                if (generatorMatch.Success)
                {
                    findings.Add(new SecurityFinding
                    {
                        Type = "Information Disclosure",
                        Description = "WordPress version exposed in generator meta tag",
                        Url = url,
                        Severity = SeverityLevel.Low,
                        Details = $"WordPress version: {generatorMatch.Groups[1].Value}"
                    });
                }

                // Check for debug information
                if (content.Contains("WP_DEBUG") || content.Contains("wp-content/debug.log"))
                {
                    findings.Add(new SecurityFinding
                    {
                        Type = "Information Disclosure",
                        Description = "Debug mode enabled",
                        Url = url,
                        Severity = SeverityLevel.Medium,
                        Details = "WordPress debug mode appears to be enabled"
                    });
                }

                // Check for PHP errors
                if (content.Contains("Fatal error:") ||
                    content.Contains("Warning:") ||
                    content.Contains("Notice:") ||
                    content.Contains("Parse error:"))
                {
                    findings.Add(new SecurityFinding
                    {
                        Type = "Information Disclosure",
                        Description = "PHP errors exposed",
                        Url = url,
                        Severity = SeverityLevel.Medium,
                        Details = "PHP errors are visible on the page"
                    });
                }

                // Check for database errors
                if (content.Contains("Database connection error") ||
                    content.Contains("MySQL") ||
                    content.Contains("mysqli_connect"))
                {
                    findings.Add(new SecurityFinding
                    {
                        Type = "Information Disclosure",
                        Description = "Database error exposed",
                        Url = url,
                        Severity = SeverityLevel.High,
                        Details = "Database connection errors are visible"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check information disclosure for {Url}", url);
            }

            return findings;
        }

        private async Task<List<SecurityFinding>> CheckXmlRpcAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var findings = new List<SecurityFinding>();

            try
            {
                var xmlrpcUrl = $"{url.TrimEnd('/')}/xmlrpc.php";
                var response = await client.GetAsync(xmlrpcUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (content.Contains("XML-RPC server accepts POST requests only"))
                    {
                        findings.Add(new SecurityFinding
                        {
                            Type = "XML-RPC Enabled",
                            Description = "XML-RPC interface is accessible",
                            Url = xmlrpcUrl,
                            Severity = SeverityLevel.Medium,
                            Details = "XML-RPC can be used for brute force attacks and DDoS amplification"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check XML-RPC for {Url}", url);
            }

            return findings;
        }

        private async Task<List<SecurityFinding>> CheckRegistrationAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var findings = new List<SecurityFinding>();

            try
            {
                var registrationUrl = $"{url.TrimEnd('/')}/wp-login.php?action=register";
                var response = await client.GetAsync(registrationUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (content.Contains("registerform") || content.Contains("Registration Form"))
                    {
                        findings.Add(new SecurityFinding
                        {
                            Type = "User Registration",
                            Description = "User registration is enabled",
                            Url = registrationUrl,
                            Severity = SeverityLevel.Medium,
                            Details = "Anyone can register new user accounts"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check registration for {Url}", url);
            }

            return findings;
        }

        private async Task<List<SecurityFinding>> CheckMultisiteAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var findings = new List<SecurityFinding>();

            try
            {
                var signupUrl = $"{url.TrimEnd('/')}/wp-signup.php";
                var response = await client.GetAsync(signupUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (content.Contains("setupform") ||
                        content.Contains("Create a New Site") ||
                        content.Contains("Get your own"))
                    {
                        findings.Add(new SecurityFinding
                        {
                            Type = "Multisite",
                            Description = "WordPress multisite detected",
                            Url = signupUrl,
                            Severity = SeverityLevel.Low,
                            Details = "This appears to be a WordPress multisite installation"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check multisite for {Url}", url);
            }

            return findings;
        }
    }
}
