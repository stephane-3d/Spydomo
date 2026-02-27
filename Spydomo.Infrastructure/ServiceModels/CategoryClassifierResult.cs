namespace Spydomo.Infrastructure.ServiceModels
{
    public class CategoryClassifierResult
    {
        public string Category { get; set; } = "";
        public string Reason { get; set; } = "";
        public double ConfidenceScore { get; set; } = 1.0;
    }

}
