namespace Spydomo.DTO
{
    public class CategoryDto
    {
        // IMPORTANT: now this is the category SLUG, e.g. "product-analytics"
        public string Primary { get; set; } = "";
        public List<string> TargetSegments { get; set; } = new();
        public List<string> UserPersonas { get; set; } = new();
        public List<string> BusinessModel { get; set; } = new(); // ["SaaS"]
        public string Reason { get; set; } = "";
        public List<string> Evidence { get; set; } = new();
        public double Confidence { get; set; }              // 0..1
        public List<CategoryAlternative> TopAlternatives { get; set; } = new();
    }
}
