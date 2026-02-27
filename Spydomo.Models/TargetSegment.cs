namespace Spydomo.Models
{
    public class TargetSegment
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!; // "SMB","MidMarket","Enterprise","Agencies"
        public virtual ICollection<CompanyTargetSegment> CompanyTargetSegments { get; set; } = new List<CompanyTargetSegment>();
    }
}
