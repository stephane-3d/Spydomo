namespace Spydomo.Models
{
    public class CanonicalTag
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;  // e.g., "support", "pricing", "automation"
        public string? Description { get; set; }  // Short explanation when/why this theme is used 
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string Slug { get; set; } = null!;
        public string? EmbeddingJson { get; set; }
    }

}
