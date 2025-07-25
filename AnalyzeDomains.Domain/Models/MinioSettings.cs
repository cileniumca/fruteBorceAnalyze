namespace AnalyzeDomains.Domain.Models
{
    public class MinioSettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string SocksConfigFileName { get; set; } = "socks-proxy-config.json";
        public bool UseSSL { get; set; } = true;
    }
}
