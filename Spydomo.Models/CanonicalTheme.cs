namespace Spydomo.Models
{
    public class CanonicalTheme
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!; // e.g., pricing_white_label_blocker
        public string? Description { get; set; }  // Short explanation when/why this theme is used
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string Slug { get; set; } = null!;

        public string? EmbeddingJson { get; set; }
    }
}
