using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using AnalyzeDomains.Domain.Models.AnalyzeModels;
using AnalyzeDomains.Domain.Models.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class DatabaseService : IDatabaseService, IDisposable
    {
        public NpgsqlDataSource _dataSource { get; set; }
        public ILogger<DatabaseService> _logger { get; set; }
        public IConfiguration _configuration { get; set; }
        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _dataSource = new NpgsqlDataSourceBuilder(_configuration.GetConnectionString("DefaultConnection")).Build();
        }
        public async Task<List<CompletedEvent>> ReadUserInfoForEvents(CancellationToken cancellationToken)
        {
            var results = new List<CompletedEvent>();
            await using (var connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                await using var cmd = new NpgsqlCommand(@"
                    SELECT cs.id, cs.domain, csu.login, cs.wp_url
                    FROM public.checked_sites cs
                    JOIN public.cheked_sites_users csu ON cs.id = csu.checked_site_id", connection);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var completedEvent = new CompletedEvent
                    {
                        SiteId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                        FullUrl = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        Login = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LoginPage = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                    };
                    results.Add(completedEvent);
                }
            }
            return results;
        }

        public async Task<List<SiteInfo>> ReadAllDomainsAsync(int batchSize = 25000, CancellationToken cancellationToken = default)
        {
            var allResults = new List<SiteInfo>();
            var tasks = new List<Task<List<SiteInfo>>>();
            int minId, maxId;
            int degreeOfParallelism = Environment.ProcessorCount;
            await using (var connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                await using var cmd = new NpgsqlCommand("SELECT MIN(id), MAX(id) FROM public.sites", connection);
                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                minId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                maxId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            }


            var ranges = new List<(int start, int end)>();
            for (int start = minId; start <= maxId; start += batchSize)
            {
                int end = Math.Min(start + batchSize - 1, maxId);
                ranges.Add((start, end));
            }


            var throttler = new SemaphoreSlim(degreeOfParallelism);

            var batchTasks = new List<Task>();
            foreach (var (start, end) in ranges)
            {
                await throttler.WaitAsync();

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var batch = await ReadBatchAsync(start, end);
                        lock (allResults)
                        {
                            allResults.AddRange(batch);
                        }
                    }
                    finally
                    {
                        throttler.Release();
                    }
                });

                batchTasks.Add(task);
            }

            await Task.WhenAll(batchTasks);
            return allResults;

        }
        private async Task<List<SiteInfo>> ReadBatchAsync(int startId, int endId)
        {
            var results = new List<SiteInfo>();
            await using var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            string sql = "SELECT id, domain FROM public.sites WHERE id BETWEEN @start AND @end and was_validated = false";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("start", startId);
            cmd.Parameters.AddWithValue("end", endId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new SiteInfo
                {
                    SiteId = reader.GetInt32(0),
                    Domain = reader.GetString(1)
                });
            }

            return results;
        }
        public async Task SiteWasValidated(SiteInfo siteInfo, CancellationToken cancellationToken)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string command = $@"UPDATE public.sites
	SET was_validated=true
	WHERE id = {siteInfo.SiteId};";
                await using var cmd = new NpgsqlCommand(command, connection, transaction);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating site validation status for site ID {SiteId}", siteInfo.SiteId);
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        public async Task<int> AddSiteWithUsers(
    SiteInfo siteInfo,
    List<WordPressLoginPage> wordPressLoginPages,
    WordPressVersion version,
    List<WordPressUser> wordPressUser,
    string fullDomain,
    CancellationToken cancellationToken
)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                const string insertSiteCommand = @"
            INSERT INTO public.checked_sites(
                date_checked, domain, wp_url, state_of_check, username, password, add_info, version, user_found)
            VALUES (@date_checked, @domain, @wp_url, @state_of_check, @username, @password, @add_info, @version, @user_found)
            RETURNING id
        ";

                var now = DateTime.UtcNow;
                var wpLoginPage = wordPressLoginPages.FirstOrDefault(x => x.Url.Contains(".php"));
                var wp_url = wpLoginPage?.Url ?? string.Empty;

                await using var command = new NpgsqlCommand(insertSiteCommand, connection, transaction);
                command.Parameters.AddRange(new[]
                {
            new NpgsqlParameter("@date_checked", now),
            new NpgsqlParameter("@domain", fullDomain),
            new NpgsqlParameter("@wp_url", wp_url),
            new NpgsqlParameter("@state_of_check", "finished"),
            new NpgsqlParameter("@username", string.Empty),
            new NpgsqlParameter("@password", string.Empty),
            new NpgsqlParameter("@add_info", JsonConvert.SerializeObject(wordPressLoginPages)),
            new NpgsqlParameter("@version", version.Version== null ? 0 : version.Version),
            new NpgsqlParameter("@user_found", wordPressUser.Count > 0)
        });
                var siteId = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);


                const string insertUserCommand = @"
            INSERT INTO public.cheked_sites_users(
                login, password, checked_site_id, user_type)
            VALUES (@login, @password, @checked_site_id, @user_type);";

                foreach (var user in wordPressUser)
                {
                    var userType = user.UserType.ToString();
                    await using var userCommand = new NpgsqlCommand(insertUserCommand, connection, transaction);
                    userCommand.Parameters.AddRange(new[]
                    {
                new NpgsqlParameter("@login", user.Username),
                new NpgsqlParameter("@password", string.Empty),
                 new NpgsqlParameter("@user_type", userType),
                new NpgsqlParameter("@checked_site_id", siteId)
            });
                    await userCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return siteId;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505") // Unique constraint violation
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

                // Site already exists, find and return the existing site ID
                const string selectExistingSiteCommand = @"
                    SELECT id FROM public.checked_sites 
                    WHERE domain = @domain";

                await using var selectCommand = new NpgsqlCommand(selectExistingSiteCommand, connection);
                selectCommand.Parameters.Add(new NpgsqlParameter("@domain", fullDomain));

                var existingSiteId = await selectCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (existingSiteId != null)
                {
                    _logger.LogInformation("Site already exists for domain {Domain}, returning existing site ID {SiteId}",
                        fullDomain, existingSiteId);
                    return (int)existingSiteId;
                }

                // If we couldn't find the existing site, throw the original exception
                throw;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public async Task InsertSiteDumpInfoAsync(int siteId, List<DbExport> dbExports, CancellationToken cancellationToken = default)
        {
            if (!dbExports.Any()) return;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                const string insertCommand = @"
                    INSERT INTO public.site_dump_info(site_id, dump_url)
                    VALUES (@site_id, @dump_url);";

                foreach (var dbExport in dbExports)
                {
                    await using var command = new NpgsqlCommand(insertCommand, connection, transaction);
                    command.Parameters.AddRange(new[]
                    {
                        new NpgsqlParameter("@site_id", siteId),
                        new NpgsqlParameter("@dump_url", dbExport.Url)
                    });
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting site dump info for site ID {SiteId}", siteId);
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        public async Task InsertSiteFilesInfoAsync(int siteId, List<SecurityFinding> securityFindings, CancellationToken cancellationToken = default)
        {
            if (!securityFindings.Any()) return;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                const string insertCommand = @"
                    INSERT INTO public.site_files_info(site_id, url, name, type, description, severity, details, discovered_at)
                    VALUES (@site_id, @url, @name, @type, @description, @severity, @details, @discovered_at);";

                foreach (var finding in securityFindings)
                {
                    await using var command = new NpgsqlCommand(insertCommand, connection, transaction);
                    command.Parameters.AddRange(new[]
                    {
                        new NpgsqlParameter("@site_id", siteId),
                        new NpgsqlParameter("@url", finding.Url),
                        new NpgsqlParameter("@name", finding.Type), // Using Type as name for backward compatibility
                        new NpgsqlParameter("@type", finding.Type),
                        new NpgsqlParameter("@description", finding.Description),
                        new NpgsqlParameter("@severity", finding.Severity.ToString()),
                        new NpgsqlParameter("@details", finding.Details),
                        new NpgsqlParameter("@discovered_at", finding.DiscoveredAt)
                    });
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting site files info for site ID {SiteId}", siteId);
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public async Task InsertSitePluginsAsync(int siteId, List<Plugin> plugins, CancellationToken cancellationToken = default)
        {
            if (!plugins.Any()) return;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                const string insertCommand = @"
                    INSERT INTO public.site_plugin(site_id, name)
                    VALUES (@site_id, @name);";

                foreach (var plugin in plugins)
                {
                    await using var command = new NpgsqlCommand(insertCommand, connection, transaction);
                    command.Parameters.AddRange(new[]
                    {
                        new NpgsqlParameter("@site_id", siteId),
                        new NpgsqlParameter("@name", plugin.Name)
                    });
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting site plugins for site ID {SiteId}", siteId);
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public async Task InsertSiteThemesAsync(int siteId, List<Theme> themes, CancellationToken cancellationToken = default)
        {
            if (!themes.Any()) return;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                const string insertCommand = @"
                    INSERT INTO public.site_theme(site_id, name)
                    VALUES (@site_id, @name);";

                foreach (var theme in themes)
                {
                    await using var command = new NpgsqlCommand(insertCommand, connection, transaction);
                    command.Parameters.AddRange(new[]
                    {
                        new NpgsqlParameter("@site_id", siteId),
                        new NpgsqlParameter("@name", theme.Name)
                    });
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting site themes for site ID {SiteId}", siteId);
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }


        public void Dispose()
        {
            _dataSource.Dispose();

        }

    }
}
