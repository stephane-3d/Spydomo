using Spydomo.Infrastructure.ServiceModels;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IFeedWriter
    {
        Task<string> GenerateFeedTextAsync(StrategicSignal signal);
    }
}
