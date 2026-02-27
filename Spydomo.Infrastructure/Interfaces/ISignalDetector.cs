using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISignalDetector
    {
        List<StrategicSignal> DetectSignals(List<SummarizedInfo> summaries);
    }
}
