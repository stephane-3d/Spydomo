namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompetitorMentionService
    {
        Task DetectMentionsInInsightAsync(int SummarizedInfoId, CancellationToken ct = default);
    }

}
