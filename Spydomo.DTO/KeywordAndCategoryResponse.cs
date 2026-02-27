namespace Spydomo.DTO
{
    public class KeywordAndCategoryResponse
    {
        public List<KeywordResult> Keywords { get; set; } = new();
        public CategoryDto Category { get; set; } = new();
    }
}
