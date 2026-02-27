namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISlackNotifier
    {
        Task NotifyAsync(string text, CancellationToken ct = default);
    }
}
