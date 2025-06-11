using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Net;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class SocksService : ISocksService
    {
        private readonly IConfiguration _configuration;
        private readonly ConcurrentDictionary<DateTime, HttpClient> _httpClientByDate = new ConcurrentDictionary<DateTime, HttpClient>();
        public SocksService(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public async Task<HttpClient> GetHttpWithSocksConnection()
        {
            HttpClient client;
            var now = DateTime.UtcNow;

            if (_httpClientByDate.Count == 0 ||
                now > _httpClientByDate.Keys.OrderBy(k => k).FirstOrDefault().AddMinutes(1))
            {
                var socksSettings = _configuration.GetSection("SocksSettings").Get<SocksSettings>();

                if (socksSettings?.Host == null)
                {
                    throw new InvalidOperationException("Socks host settings are not properly configured");
                }

                Uri uri = new Uri($"http://{socksSettings.Host}");
                var httpClientHandler = new HttpClientHandler()
                {
                    Proxy = new WebProxy()
                    {
                        Address = uri,
                        Credentials = new NetworkCredential(_configuration["SocksSettings:UserName"], _configuration["SocksSettings:Password"]),
                    }
                };

                client = new HttpClient(httpClientHandler)
                {
                    Timeout = TimeSpan.FromMinutes(1)
                };

                _httpClientByDate.Clear();
                _httpClientByDate.TryAdd(now, client);
            }
            else
            {
                client = _httpClientByDate.OrderByDescending(x => x.Key).FirstOrDefault().Value;
            }

            return client;
        }
    }
}
