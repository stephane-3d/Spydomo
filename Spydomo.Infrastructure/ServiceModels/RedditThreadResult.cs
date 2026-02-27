namespace Spydomo.Infrastructure.ServiceModels
{
    public class RedditThreadResult
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public List<string> TopComments { get; set; }
        public DateTime? CreatedUtc { get; set; }
    }
}
