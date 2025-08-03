using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models.AnalyzeModels;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class DbExportDetector : IDbExportDetector
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DbExportDetector> _logger;
        private readonly ISocksService _socksService;
        // SQL patterns that indicate database exports
        private static readonly Regex SqlPattern = new(@"(?:DROP|(?:UN)?LOCK|CREATE|ALTER)\s+(?:TABLE|DATABASE)|INSERT\s+INTO",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Common database export file paths
        private readonly List<string> _dbExportPaths = new()
    {
        "{domain_name}.sql",
        "wordpress.sql",
        "backup.sql",
        "database.sql",
        "dump.sql",
        "mysql.sql",
        "backup/{domain_name}.sql",
        "backup/{domain_name}.zip",
        "backup/mysql.sql",
        "backup/wordpress.sql",
        "backup/database.sql",
        "backup/backup.sql",
        "backups/{domain_name}.sql",
        "backups/{domain_name}.sql.gz",
        "backups/{domain_name}.zip",
        "backups/db_backup.sql",
        "backups/mysql.sql",
        "backups/wordpress.sql",
        "backups/database.sql",
        "db/{domain_name}.sql",
        "db/backup.sql",
        "db/mysql.sql",
        "data/{domain_name}.sql",
        "data/backup.sql",
        "sql/{domain_name}.sql",
        "sql/backup.sql",
        "wp-content/backup.sql",
        "wp-content/uploads/{domain_name}.sql",
        "wp-content/uploads/backup.sql",
        "uploads/{domain_name}.sql",
        "uploads/backup.sql"
    };

        public DbExportDetector(IHttpClientFactory httpClientFactory,
                               ILogger<DbExportDetector> logger,
                               ISocksService socksService
                               )
        {
            _socksService = socksService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

        }

        public async Task<List<DbExport>> DetectDbExportsAsync(string url, CancellationToken cancellationToken = default)
        {
            var dbExports = new List<DbExport>();
            var client = await _socksService.GetHttpWithBalancedSocksConnection();

            try
            {
                _logger.LogInformation("Starting database export detection for {Url}", url);

                var domainName = ExtractDomainName(url);
                var domainNameWithSub = ExtractDomainNameWithSub(url);

                var tasks = new List<Task<DbExport?>>();

                foreach (var path in _dbExportPaths)
                {
                    // Replace domain name placeholders
                    var actualPath = path.Replace("{domain_name}", domainName);
                    tasks.Add(CheckDbExportAsync(client, url, actualPath, cancellationToken));

                    // Also check with subdomain if different
                    if (domainName != domainNameWithSub)
                    {
                        var subPath = path.Replace("{domain_name}", domainNameWithSub);
                        tasks.Add(CheckDbExportAsync(client, url, subPath, cancellationToken));
                    }
                }

                var results = await Task.WhenAll(tasks);
                dbExports.AddRange(results.Where(r => r != null)!);

                _logger.LogInformation("Found {DbExportCount} database exports for {Url}", dbExports.Count, url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting database exports for {Url}", url);
            }

            return dbExports;
        }

        private async Task<DbExport?> CheckDbExportAsync(HttpClient client, string baseUrl, string path, CancellationToken cancellationToken)
        {
            try
            {
                var fullUrl = $"{baseUrl.TrimEnd('/')}/{path}";

                // First check if the file exists with HEAD request
                var headResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, fullUrl), cancellationToken);

                if (!headResponse.IsSuccessStatusCode)
                    return null;

                // Check content type for ZIP files
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var contentType = headResponse.Content.Headers.ContentType?.MediaType;
                    if (contentType != null && contentType.Contains("application/zip", StringComparison.OrdinalIgnoreCase))
                    {
                        return new DbExport
                        {
                            Url = fullUrl,
                            Type = "ZIP Archive",
                            DetectionMethod = "Direct Access - HEAD Request",
                            Confidence = ConfidenceLevel.High,
                            Size = headResponse.Content.Headers.ContentLength ?? 0
                        };
                    }
                    return null;
                }

                // For SQL files, get partial content to check for SQL patterns
                var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                request.Headers.Add("Range", "bytes=0-3000"); // Only get first 3KB

                var response = await client.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                // Check if content matches SQL patterns
                if (SqlPattern.IsMatch(content))
                {
                    return new DbExport
                    {
                        Url = fullUrl,
                        Type = "SQL Database Export",
                        DetectionMethod = "Direct Access - Content Analysis",
                        Confidence = ConfidenceLevel.High,
                        Size = response.Content.Headers.ContentLength ?? content.Length,
                        Preview = content.Length > 200 ? content.Substring(0, 200) + "..." : content
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check database export at {Path}", path);
            }

            return null;
        }

        private string ExtractDomainName(string url)
        {
            try
            {
                var uri = new Uri(url);
                var host = uri.Host;

                // If it's an IP address, return as-is
                if (System.Net.IPAddress.TryParse(host, out _))
                    return host;

                // Extract domain name (without subdomain and TLD)
                var parts = host.Split('.');
                if (parts.Length >= 2)
                {
                    // For domains like "example.com" or "www.example.com"
                    // Return "example" part
                    var domainPart = parts.Length > 2 ? parts[^2] : parts[0];
                    return domainPart;
                }

                return host;
            }
            catch
            {
                return "unknown";
            }
        }

        private string ExtractDomainNameWithSub(string url)
        {
            try
            {
                var uri = new Uri(url);
                var host = uri.Host;

                // If it's an IP address, return as-is
                if (System.Net.IPAddress.TryParse(host, out _))
                    return host;

                var parts = host.Split('.');
                if (parts.Length > 2 && !IsCommonSubdomain(parts[0]))
                {
                    // For domains like "app.example.com", return "app.example"
                    return $"{parts[0]}.{parts[1]}";
                }

                // For domains like "example.com" or "www.example.com"
                return ExtractDomainName(url);
            }
            catch
            {
                return "unknown";
            }
        }

        private static bool IsCommonSubdomain(string subdomain)
        {
            var commonSubdomains = new[] { "www", "mail", "ftp", "m", "mobile", "api" };
            return commonSubdomains.Contains(subdomain.ToLower());
        }
    }
}
