using AnalyzeDomains.Domain.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Net;

namespace AnalyzeDomains.Infrastructure.Services
{
    public class SocksService : ISocksService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ConcurrentDictionary<DateTime, string> _ipByDate = new ConcurrentDictionary<DateTime, string>();
        public SocksService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }
        public async Task<HttpClient> GetHttpWithSocksConnection()
        {
            var httpClientHandler = new HttpClientHandler()
            {
                Proxy = new WebProxy()
                {
                    Address = new Uri(_configuration["SocksSettings:Host"]),
                    Credentials = new NetworkCredential(_configuration["SocksSettings:UserName"], _configuration["SocksSettings:Password"]),
                }
            };
            var client = _httpClientFactory.CreateClient("SocksClient");
            client.Timeout = TimeSpan.FromMinutes(1);
            return client;
        }
    }
}
