using Spydomo.Models;

namespace Spydomo.Infrastructure.ServiceModels
{
    public class ThemeNormalizerResult
    {
        public string RawTheme { get; set; } = null!;
        public CanonicalTheme CanonicalTheme { get; set; } = null!;
        public double ConfidenceScore { get; set; } = 1.0;
        public bool IsNewCanonical { get; set; } = false;
    }
}
