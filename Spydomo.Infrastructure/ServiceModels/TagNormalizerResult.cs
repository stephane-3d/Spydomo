using Spydomo.Models;

namespace Spydomo.Infrastructure.ServiceModels
{
    public class TagNormalizerResult
    {
        public CanonicalTag? CanonicalTag { get; set; }
        public string RawTag { get; set; } = "";
        public string Sentiment { get; set; } = ""; // "+", "-", or ""
        public double ConfidenceScore { get; set; } = 1.0;
        public bool IsNewCanonical { get; set; } = false;
    }

}
