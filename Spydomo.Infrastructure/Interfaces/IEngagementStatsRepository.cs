namespace Spydomo.Infrastructure.Interfaces
{
    public interface IEngagementStatsRepository
    {
        Task<double> GetBaselineAsync(int companyId, int? sourceTypeId, DateTime nowUtc, string periodType = "30d", CancellationToken ct = default);
    }
}
