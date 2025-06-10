namespace AnalyzeDomains.Domain.Interfaces.Services
{
    public interface ISocksService
    {
        Task<HttpClient> GetHttpWithSocksConnection();
    }
}
