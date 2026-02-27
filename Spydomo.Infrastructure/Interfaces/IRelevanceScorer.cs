using Spydomo.Infrastructure.ServiceModels;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IRelevanceScorer
    {
        double Score(StrategicSignal signal);
    }
}
