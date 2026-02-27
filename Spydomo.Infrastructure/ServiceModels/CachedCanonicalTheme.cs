namespace Spydomo.Infrastructure.ServiceModels
{
    public class CachedCanonicalTheme
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public List<float> Embedding { get; set; } = null!;
    }

}
