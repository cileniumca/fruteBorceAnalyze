using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class VersionAnalyzer : IVersionAnalyzer
    {
        private readonly ISocksService _socksService;
        private readonly ILogger<VersionAnalyzer> _logger;
        private readonly VersionDetectionSettings _settings;
        public VersionAnalyzer(ISocksService socksService, ILogger<VersionAnalyzer> logger)
        {
            _socksService = socksService;
            _logger = logger;
            _settings = new VersionDetectionSettings();
        }

        public async Task<WordPressVersion?> DetectVersionAsync(string url, DetectionMode mode, ConfidenceLevel minConfidence, CancellationToken cancellationToken = default)
        {
            var detectedVersions = new List<WordPressVersion>();
            _logger.LogDebug("Starting version detection for {Url} with mode {Mode}", url, mode);

            try
            {
                _logger.LogDebug("Starting version detection for {Url} with mode {Mode}", url, mode);

                // Method 1: Check meta generator (passive)
                if (mode == DetectionMode.Passive || mode == DetectionMode.Mixed)
                {
                    var metaVersion = await DetectFromMetaGenerator(url, cancellationToken);
                    if (metaVersion != null)
                        detectedVersions.Add(metaVersion);
                    if (IsVersionFound(detectedVersions, minConfidence))
                    {
                        return detectedVersions
                    .Where(v => v.Confidence >= minConfidence)
                    .OrderByDescending(v => v.Confidence)
                    .ThenByDescending(v => IsNewerVersion(v.Version))
                    .FirstOrDefault();
                    }
                }

                // Method 2: Check readme.html (aggressive)
                if (mode == DetectionMode.Aggressive || mode == DetectionMode.Mixed)
                {
                    var readmeVersion = await DetectFromReadme(url, cancellationToken);
                    if (readmeVersion != null)
                        detectedVersions.Add(readmeVersion);

                    if (IsVersionFound(detectedVersions, minConfidence))
                    {
                        return detectedVersions
                    .Where(v => v.Confidence >= minConfidence)
                    .OrderByDescending(v => v.Confidence)
                    .ThenByDescending(v => IsNewerVersion(v.Version))
                    .FirstOrDefault();
                    }
                }

                // Method 3: Check CSS/JS version parameters (passive/mixed)
                if (mode != DetectionMode.Aggressive)
                {
                    var assetVersions = await DetectFromAssets(url, cancellationToken);
                    detectedVersions.AddRange(assetVersions);

                    if (IsVersionFound(detectedVersions, minConfidence))
                    {
                        return detectedVersions
                    .Where(v => v.Confidence >= minConfidence)
                    .OrderByDescending(v => v.Confidence)
                    .ThenByDescending(v => IsNewerVersion(v.Version))
                    .FirstOrDefault();
                    }
                }

                // Method 4: Check version.php (aggressive)
                if (mode == DetectionMode.Aggressive)
                {
                    var phpVersion = await DetectFromVersionPhp(url, cancellationToken);
                    if (phpVersion != null)
                        detectedVersions.Add(phpVersion);

                    if (IsVersionFound(detectedVersions, minConfidence))
                    {
                        return detectedVersions
                    .Where(v => v.Confidence >= minConfidence)
                    .OrderByDescending(v => v.Confidence)
                    .ThenByDescending(v => IsNewerVersion(v.Version))
                    .FirstOrDefault();
                    }
                }

                // Method 5: Check theme versions (mixed/aggressive)
                if (mode == DetectionMode.Mixed || mode == DetectionMode.Aggressive)
                {
                    var themeVersions = await DetectFromThemes(url, cancellationToken);
                    detectedVersions.AddRange(themeVersions);

                    if (IsVersionFound(detectedVersions, minConfidence))
                    {
                        return detectedVersions
                    .Where(v => v.Confidence >= minConfidence)
                    .OrderByDescending(v => v.Confidence)
                    .ThenByDescending(v => IsNewerVersion(v.Version))
                    .FirstOrDefault();
                    }
                }

                // Return the highest confidence version that meets the minimum threshold
                var bestVersion = detectedVersions
                    .Where(v => v.Confidence >= minConfidence)
                    .OrderByDescending(v => v.Confidence)
                    .ThenByDescending(v => IsNewerVersion(v.Version))
                    .FirstOrDefault();

                if (bestVersion != null)
                {
                    _logger.LogInformation("Detected WordPress version {Version} with {Confidence} confidence via {Method}",
                        bestVersion.Version, bestVersion.Confidence, bestVersion.DetectionMethod);
                }

                return bestVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during version detection for {Url}", url);
                return null;
            }
        }
        private static bool IsVersionFound(List<WordPressVersion> versions, ConfidenceLevel confidenceLevel)
        {
            if (versions.Any(x => x.Confidence >= confidenceLevel))
            {
                return true;
            }
            return false;
        }
        private async Task<WordPressVersion?> DetectFromMetaGenerator(string url, CancellationToken cancellationToken)
        {
            try
            {
                var client = await _socksService.GetHttpWithSocksConnection();

                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var regex = new Regex(@"<meta[^>]*name\s*=\s*[""']generator[""'][^>]*content\s*=\s*[""']WordPress\s+([\d\.]+)[^""']*[""']",
                    RegexOptions.IgnoreCase);

                var match = regex.Match(content);
                if (match.Success)
                {
                    return new WordPressVersion
                    {
                        Version = match.Groups[1].Value,
                        Confidence = ConfidenceLevel.High,
                        DetectionMethod = "Meta Generator",
                        Source = url,
                        Metadata = { ["html_content"] = true }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error detecting version from meta generator");
            }

            return null;
        }

        private async Task<WordPressVersion?> DetectFromReadme(string url, CancellationToken cancellationToken)
        {
            try
            {
                var client = await _socksService.GetHttpWithSocksConnection();
                var readmeUrl = $"{url.TrimEnd('/')}/readme.html";
                var response = await client.GetAsync(readmeUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var regex = new Regex(@"<br\s*/>\s*Version\s+([\d\.]+)", RegexOptions.IgnoreCase);

                var match = regex.Match(content);
                if (match.Success)
                {
                    return new WordPressVersion
                    {
                        Version = match.Groups[1].Value,
                        Confidence = ConfidenceLevel.Certain,
                        DetectionMethod = "Readme File",
                        Source = readmeUrl,
                        Metadata = { ["file_accessible"] = true }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error detecting version from readme.html");
            }

            return null;
        }

        private async Task<List<WordPressVersion>> DetectFromAssets(string url, CancellationToken cancellationToken)
        {
            var versions = new List<WordPressVersion>();

            try
            {
                var client = await _socksService.GetHttpWithSocksConnection();
                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return versions;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                // Look for versioned CSS/JS files
                var cssRegex = new Regex(@"wp-[^""']*\.css\?ver=([\d\.]+)", RegexOptions.IgnoreCase);
                var jsRegex = new Regex(@"wp-[^""']*\.js\?ver=([\d\.]+)", RegexOptions.IgnoreCase);

                foreach (Match match in cssRegex.Matches(content))
                {
                    versions.Add(new WordPressVersion
                    {
                        Version = match.Groups[1].Value,
                        Confidence = ConfidenceLevel.Medium,
                        DetectionMethod = "CSS Version Parameter",
                        Source = url,
                        Metadata = { ["asset_type"] = "css" }
                    });
                }

                foreach (Match match in jsRegex.Matches(content))
                {
                    versions.Add(new WordPressVersion
                    {
                        Version = match.Groups[1].Value,
                        Confidence = ConfidenceLevel.Medium,
                        DetectionMethod = "JS Version Parameter",
                        Source = url,
                        Metadata = { ["asset_type"] = "js" }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error detecting version from assets");
            }

            return versions;
        }

        private async Task<WordPressVersion?> DetectFromVersionPhp(string url, CancellationToken cancellationToken)
        {
            try
            {
                var client = await _socksService.GetHttpWithSocksConnection();
                var versionUrl = $"{url.TrimEnd('/')}/wp-includes/version.php";
                var response = await client.GetAsync(versionUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var regex = new Regex(@"\$wp_version\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);

                var match = regex.Match(content);
                if (match.Success)
                {
                    return new WordPressVersion
                    {
                        Version = match.Groups[1].Value,
                        Confidence = ConfidenceLevel.Certain,
                        DetectionMethod = "Version PHP File",
                        Source = versionUrl,
                        Metadata = { ["direct_access"] = true }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error detecting version from version.php");
            }

            return null;
        }

        private async Task<List<WordPressVersion>> DetectFromThemes(string url, CancellationToken cancellationToken)
        {
            var versions = new List<WordPressVersion>();
            var commonThemes = new[] { "twentyten", "twentyeleven", "twentytwelve", "twentythirteen", "twentyfourteen", "twentyfifteen", "twentysixteen", "twentyseventeen", "twentynineteen", "twentytwenty", "twentytwentyone", "twentytwentytwo", "twentytwentythree", "twentytwentyfour", "twentytwentyfive" };
            var client = await _socksService.GetHttpWithSocksConnection();
            foreach (var theme in commonThemes)
            {
                try
                {
                    var themeUrl = $"{url.TrimEnd('/')}/wp-content/themes/{theme}/style.css";
                    var response = await client.GetAsync(themeUrl, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        continue;

                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var regex = new Regex(@"Version:\s*([\d\.]+)", RegexOptions.IgnoreCase);

                    var match = regex.Match(content);
                    if (match.Success)
                    {
                        versions.Add(new WordPressVersion
                        {
                            Version = InferWordPressVersionFromTheme(theme, match.Groups[1].Value),
                            Confidence = ConfidenceLevel.Medium,
                            DetectionMethod = $"Theme Version ({theme})",
                            Source = themeUrl,
                            Metadata = { ["theme_name"] = theme, ["theme_version"] = match.Groups[1].Value }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error detecting version from theme {Theme}", theme);
                }
            }

            return versions;
        }

        private static bool IsNewerVersion(string? version)
        {
            if (string.IsNullOrEmpty(version))
                return false;

            if (Version.TryParse(version, out var v))
                return v.Major >= 5; // Prefer WordPress 5.0+

            return false;
        }

        private static string InferWordPressVersionFromTheme(string theme, string themeVersion)
        {
            return theme.ToLower() switch
            {
                "twentyten" => "3.0",
                "twentyeleven" => "3.2",
                "twentytwelve" => "3.5",
                "twentythirteen" => "3.6",
                "twentyfourteen" => "3.8",
                "twentyfifteen" => "4.1",
                "twentysixteen" => "4.4",
                "twentyseventeen" => "4.7",
                "twentynineteen" => "5.0",
                "twentytwenty" => "5.3",
                "twentytwentyone" => "5.6",
                "twentytwentytwo" => "5.9",
                "twentytwentythree" => "6.1",
                "twentytwentyfour" => "6.4",
                "twentytwentyfive" => "6.7",
                _ => themeVersion
            };
        }
    }

    public class VersionDetectionSettings
    {
        public List<string> CommonPaths { get; set; } = new()
    {
        "/readme.html",
        "/wp-includes/version.php",
        "/wp-admin/css/wp-admin.min.css",
        "/wp-content/themes/twentytwentyone/style.css",
        "/wp-content/themes/twentytwentytwo/style.css",
        "/wp-content/themes/twentytwentythree/style.css",
        "/wp-includes/js/jquery/jquery.min.js",
        "/wp-includes/css/dashicons.min.css"
    };

        public List<string> MetaGenerators { get; set; } = new()
    {
        "WordPress",
        "wp-",
        "wordpress"
    };

        public Dictionary<string, string> VersionPatterns { get; set; } = new()
        {
            ["readme.html"] = @"<br />\s*Version\s+([\d\.]+)",
            ["version.php"] = @"\$wp_version\s*=\s*['""]([^'""]+)['""]",
            ["css"] = @"wp-admin\.min\.css\?ver=([\d\.]+)",
            ["js"] = @"jquery\.min\.js\?ver=([\d\.]+)"
        };

    }

}
