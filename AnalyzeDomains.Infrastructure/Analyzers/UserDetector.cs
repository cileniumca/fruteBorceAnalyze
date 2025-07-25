using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class UserDetector : IUserDetector
    {
        private readonly ISocksService _socksService;
        private readonly ILogger<UserDetector> _logger;
        private readonly UserEnumerationSettings _settings;
        public UserDetector(ISocksService socksService, ILogger<UserDetector> logger)
        {
            _logger = logger;
            _settings = new UserEnumerationSettings();
            _socksService = socksService;
        }
        public async Task<List<WordPressUser>> EnumerateUsersAsync(string url, DetectionMode mode, int maxUsers, CancellationToken cancellationToken = default)
        {
            var users = new List<WordPressUser>();

            try
            {
                _logger.LogDebug("Starting user enumeration for {Url} with mode {Mode}", url, mode);

                // Method 1: WP-JSON API (passive/mixed)
                if (mode != DetectionMode.Aggressive)
                {
                    var jsonUsers = await EnumerateViaWpJson(url, maxUsers, cancellationToken);
                    users.AddRange(jsonUsers);
                }

                // Method 2: Author archives (aggressive/mixed)
                if (mode != DetectionMode.Passive && users.Count < maxUsers)
                {
                    var archiveUsers = await EnumerateViaAuthorArchives(url, maxUsers - users.Count, cancellationToken);
                    users.AddRange(archiveUsers);
                }

                // Method 3: XML-RPC (aggressive)
                if (mode != DetectionMode.Aggressive && users.Count < maxUsers)
                {
                    var xmlRpcUsers = await EnumerateViaXmlRpc(url, maxUsers - users.Count, cancellationToken);
                    users.AddRange(xmlRpcUsers);
                }

                // Method 4: Login redirect enumeration (aggressive)
                if (mode != DetectionMode.Aggressive && users.Count < maxUsers)
                {
                    var loginUsers = await EnumerateViaLoginRedirect(url, maxUsers - users.Count, cancellationToken);
                    users.AddRange(loginUsers);
                }
                var tempUsers = new List<WordPressUser>();
                foreach (var user in users)
                {
                    if (Int32.TryParse(user.Username, out _))
                    {
                        continue;
                    }
                    else
                    {
                        tempUsers.Add(user);
                    }
                }
                // Remove duplicates and return
                var uniqueUsers = tempUsers
                    .GroupBy(u => u.Username.ToLower())
                    .Select(g => g.OrderByDescending(u => u.Confidence).First())
                    .Take(maxUsers)
                    .ToList();

                _logger.LogInformation("Found {UserCount} unique WordPress users", uniqueUsers.Count);
                return uniqueUsers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user enumeration for {Url}", url);
                return users;
            }
        }

        private async Task<List<WordPressUser>> EnumerateViaWpJson(string url, int maxUsers, CancellationToken cancellationToken)
        {
            var users = new List<WordPressUser>();

            try
            {
                var apiUrl = $"{url.TrimEnd('/')}/wp-json/wp/v2/users";
                _logger.LogDebug("Attempting user enumeration via WP-JSON API: {ApiUrl}", apiUrl);
                var client = await _socksService.GetHttpWithBalancedSocksConnection();

                var response = await client.GetAsync(apiUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("WP-JSON API returned {StatusCode}", response.StatusCode);
                    return users;
                }
                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var jsonUsers = JsonSerializer.Deserialize<JsonElement[]>(jsonContent);

                if (jsonUsers != null)
                {
                    foreach (var userJson in jsonUsers.Take(maxUsers))
                    {
                        var user = new WordPressUser
                        {
                            Id = userJson.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0,
                            Username = userJson.TryGetProperty("slug", out var slugProp) ? slugProp.GetString() ?? "" : "",
                            DisplayName = userJson.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
                            Confidence = ConfidenceLevel.Certain,
                            DetectionMethod = "WP-JSON API",
                            Metadata = { ["api_endpoint"] = apiUrl },
                            UserType = EventType.WpLoginCompleted
                        };
                        if (userJson.TryGetProperty("link", out var linkProp))
                        {
                            user.Metadata["profile_link"] = linkProp.GetString() ?? "";
                        }

                        if (!string.IsNullOrEmpty(user.Username))
                        {
                            users.Add(user);
                            _logger.LogDebug("Found user via WP-JSON: {Username} (ID: {Id})", user.Username, user.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error enumerating users via WP-JSON");
            }

            return users;
        }

        private async Task<List<WordPressUser>> EnumerateViaAuthorArchives(string url, int maxUsers, CancellationToken cancellationToken)
        {
            var users = new List<WordPressUser>();

            try
            {
                _logger.LogDebug("Attempting user enumeration via author archives"); var client = await _socksService.GetHttpWithBalancedSocksConnection();
                for (int userId = 1; userId <= Math.Min(maxUsers * 2, 100); userId++)
                {
                    if (users.Count >= maxUsers)
                        break;

                    var authorUrl = $"{url.TrimEnd('/')}/author/{userId}/";
                    var response = await client.GetAsync(authorUrl, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken);

                        // Extract username from URL or content
                        var username = ExtractUsernameFromAuthorPage(content, response.RequestMessage?.RequestUri?.ToString());

                        if (!string.IsNullOrEmpty(username))
                        {
                            var user = new WordPressUser
                            {
                                Id = userId,
                                Username = username,
                                Confidence = ConfidenceLevel.High,
                                DetectionMethod = "Author Archive",
                                Metadata = { ["author_url"] = authorUrl },
                                UserType = EventType.WpLoginCompleted
                            };

                            users.Add(user);
                            _logger.LogDebug("Found user via author archive: {Username} (ID: {Id})", username, userId);
                        }
                    }

                    // Add delay to avoid overwhelming the server
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error enumerating users via author archives");
            }

            return users;
        }

        private async Task<List<WordPressUser>> EnumerateViaXmlRpc(string url, int maxUsers, CancellationToken cancellationToken)
        {
            var users = new List<WordPressUser>();
            var client = await _socksService.GetHttpWithBalancedSocksConnection();
            try
            {
                var xmlRpcUrl = $"{url.TrimEnd('/')}/xmlrpc.php";
                _logger.LogDebug("Attempting user enumeration via XML-RPC: {XmlRpcUrl}", xmlRpcUrl);

                // Check if XML-RPC is available
                var checkResponse = await client.GetAsync(xmlRpcUrl, cancellationToken);
                if (!checkResponse.IsSuccessStatusCode)
                    return users;

                var checkContent = await checkResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!checkContent.Contains("XML-RPC", StringComparison.OrdinalIgnoreCase))
                    return users;

                // Try common usernames via XML-RPC method calls
                var commonUsernames = new[] { "admin", "administrator", "user", "test", "demo", "wordpress" };

                foreach (var username in commonUsernames.Take(maxUsers))
                {
                    if (await CheckUsernameViaXmlRpc(xmlRpcUrl, username, cancellationToken))
                    {
                        users.Add(new WordPressUser
                        {
                            Username = username,
                            Confidence = ConfidenceLevel.Medium,
                            DetectionMethod = "XML-RPC",
                            Metadata = { ["xmlrpc_url"] = xmlRpcUrl },
                            UserType = EventType.XmlRpcCompleted
                        });

                        _logger.LogDebug("Found user via XML-RPC: {Username}", username);
                    }

                    await Task.Delay(200, cancellationToken); // Longer delay for XML-RPC
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error enumerating users via XML-RPC");
            }

            return users;
        }
        private async Task<List<WordPressUser>> EnumerateViaLoginRedirect(string url, int maxUsers, CancellationToken cancellationToken)
        {
            var users = new List<WordPressUser>();
            var client = await _socksService.GetHttpWithBalancedSocksConnection();
            try
            {
                var loginUrl = $"{url.TrimEnd('/')}/wp-login.php";
                _logger.LogDebug("Attempting user enumeration via login redirect");

                var commonUsernames = new[] { "admin", "administrator", "user", "test", "demo", "wordpress", "wp", "root" };

                foreach (var username in commonUsernames.Take(maxUsers))
                {
                    var formData = new FormUrlEncodedContent(new[]
                    {
                    new KeyValuePair<string, string>("log", username),
                    new KeyValuePair<string, string>("pwd", "invalidpassword"),
                        new KeyValuePair<string, string>("wp-submit", "Log In")
                    });

                    var response = await client.PostAsync(loginUrl, formData, cancellationToken);
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Different error messages indicate if username exists
                    if (responseContent.Contains("password you entered", StringComparison.OrdinalIgnoreCase) ||
                        responseContent.Contains("incorrect password", StringComparison.OrdinalIgnoreCase))
                    {
                        users.Add(new WordPressUser
                        {
                            Username = username,
                            Confidence = ConfidenceLevel.High,
                            DetectionMethod = "Login Error Analysis",
                            Metadata = { ["login_url"] = loginUrl },
                            UserType = EventType.WpLoginCompleted
                        });

                        _logger.LogDebug("Found user via login redirect: {Username}", username);
                    }

                    await Task.Delay(500, cancellationToken); // Longer delay for login attempts
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error enumerating users via login redirect");
            }

            return users;
        }

        private static string? ExtractUsernameFromAuthorPage(string content, string? url)
        {
            // Try to extract username from URL first
            if (!string.IsNullOrEmpty(url))
            {
                var urlMatch = Regex.Match(url, @"/author/([^/]+)", RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                    return urlMatch.Groups[1].Value;
            }

            // Try to extract from content
            var patterns = new[]
            {
            @"<title>([^<]*?)\s*[-–|]\s*",
            @"class=[""']author[""'][^>]*>([^<]+)<",
            @"<h1[^>]*class=[""'][^""']*author[^""']*[""'][^>]*>([^<]+)</h1>",
        };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var username = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(username) && username.Length < 50)
                        return username;
                }
            }

            return null;
        }
        private async Task<bool> CheckUsernameViaXmlRpc(string xmlRpcUrl, string username, CancellationToken cancellationToken)
        {
            var client = await _socksService.GetHttpWithBalancedSocksConnection();
            try
            {
                var xmlRequest = $@"<?xml version=""1.0""?>
<methodCall>
    <methodName>wp.getProfile</methodName>
    <params>
        <param><value><string>{username}</string></value>
        <param><value><string>invalidpassword</string></value>
    </params>
</methodCall>";

                var content = new StringContent(xmlRequest, System.Text.Encoding.UTF8, "text/xml");
                var response = await client.PostAsync(xmlRpcUrl, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    // If we get a specific error about password vs username, the user exists
                    return responseContent.Contains("faultCode") &&
                           !responseContent.Contains("Invalid username");
                }
            }
            catch
            {
                // Ignore errors for XML-RPC checks
            }

            return false;
        }
    }

}
