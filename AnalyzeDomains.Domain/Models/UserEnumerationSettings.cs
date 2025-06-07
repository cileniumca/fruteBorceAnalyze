namespace AnalyzeDomains.Domain.Models
{
    public class UserEnumerationSettings
    {
        public List<string> EnumerationMethods { get; set; } = new()
    {
        "wp-json",
        "author-archives",
        "xml-rpc",
        "login-redirect"
    };

        public string WpJsonEndpoint { get; set; } = "/wp-json/wp/v2/users";
        public string AuthorArchivePattern { get; set; } = "/?author={0}";
        public string XmlRpcEndpoint { get; set; } = "/xmlrpc.php";
        public int MaxUsersToEnumerate { get; set; } = 10;
        public List<int> UserIdsToCheck { get; set; } = new();
    }
}
