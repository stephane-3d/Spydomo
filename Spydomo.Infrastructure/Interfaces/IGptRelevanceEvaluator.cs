namespace Spydomo.Infrastructure.Interfaces
{
    public interface IGptRelevanceEvaluator
    {
        Task<bool> EvaluateRelevanceAsync(int companyId, string companyName, string content, List<string> keywords);
    }

}
