namespace Spydomo.DTO
{
    public sealed class RenderHtmlResponse
    {
        public bool Ok { get; set; }
        public string? Url { get; set; }
        public string? FinalUrl { get; set; }
        public string? Html { get; set; }
        public List<string>? Links { get; set; }
        public List<string>? SocialLinks { get; set; }
    }

}
