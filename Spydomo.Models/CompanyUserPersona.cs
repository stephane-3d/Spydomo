namespace Spydomo.Models
{
    public class CompanyUserPersona
    {
        public int CompanyId { get; set; }
        public int UserPersonaId { get; set; }
        public virtual Company Company { get; set; } = null!;
        public virtual UserPersona UserPersona { get; set; } = null!;
    }
}
