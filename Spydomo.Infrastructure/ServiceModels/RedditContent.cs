namespace Spydomo.Infrastructure.ServiceModels
{
    public class RedditContent
    {
        public string Title { get; set; }
        public string Text { get; set; }              // Post body (selftext)
        public string Url { get; set; }
        public string Subreddit { get; set; }
        public string Author { get; set; }
        public int Upvotes { get; set; }
        public DateTime CreatedUtc { get; set; }
        public List<RedditComment> Comments { get; set; } = new();
    }

    public class RedditComment
    {
        public string Body { get; set; }
        public string Author { get; set; }
        public int Upvotes { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public class RedditSearchResult
    {
        public RedditSearchData Data { get; set; }
    }

    public class RedditSearchData
    {
        public List<RedditPostWrapper> Children { get; set; }
    }

    public class RedditPostWrapper
    {
        public RedditPost Data { get; set; }
    }

    public class RedditPost
    {
        public string Title { get; set; }
        public string Selftext { get; set; }
        public string Permalink { get; set; }
        public string Subreddit { get; set; }
        public string Author { get; set; }
        public double Created_Utc { get; set; }
        public int? Score { get; set; }
        public int Num_Comments { get; set; }
    }
}
