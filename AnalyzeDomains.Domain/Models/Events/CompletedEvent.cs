namespace AnalyzeDomains.Domain.Models.Events
{
    public class CompletedEvent
    {
        public int SiteId { get; set; }
        public string FullUrl { get; set; }
        public string LoginPage { get; set; }
        public string Login { get; set; }
    }
}
