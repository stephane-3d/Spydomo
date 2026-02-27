namespace Spydomo.DTO
{
    public class CategoryAlternative
    {
        public string Primary { get; set; } = "";  // slug
        public double Confidence { get; set; } // 0..1
    }
}
