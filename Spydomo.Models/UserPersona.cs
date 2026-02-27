namespace Spydomo.Models
{
    public class UserPersona
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!; // "Marketing","Support","Product","Sales","DataAnalytics","Engineering"
        public virtual ICollection<CompanyUserPersona> CompanyUserPersonas { get; set; } = new List<CompanyUserPersona>();
    }
}
