namespace Spydomo.Models
{
    public class CompanyTargetSegment
    {
        public int CompanyId { get; set; }
        public int TargetSegmentId { get; set; }
        public virtual Company Company { get; set; } = null!;
        public virtual TargetSegment TargetSegment { get; set; } = null!;
    }
}
