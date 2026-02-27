using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Models;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class AccountCleanupService
    {
        private readonly ILogger<AccountCleanupService> _logger;
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;

        public AccountCleanupService(
            ILogger<AccountCleanupService> logger,
            IDbContextFactory<SpydomoContext> dbFactory)
        {
            _logger = logger;
            _dbFactory = dbFactory;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 60 * 10)]
        // Hangfire-friendly (supports cancellation on shutdown)
        public async Task RunAsync(IJobCancellationToken hangfireToken)
        {
            _logger.LogInformation("⏱ AccountCleanupService > RunAsync started");

            var ct = hangfireToken?.ShutdownToken ?? CancellationToken.None;

            try
            {
                // 1) Get the batch of clientIds to cleanup (short-lived context)
                List<int> clientIds;
                await using (var db = await _dbFactory.CreateDbContextAsync(ct))
                {
                    clientIds = await db.Clients
                        .AsNoTracking()
                        .Where(c => c.DeletionRequestedAt != null && c.CleanupCompletedAt == null)
                        .OrderBy(c => c.DeletionRequestedAt)
                        .Take(25)
                        .Select(c => c.Id)
                        .ToListAsync(ct);
                }

                if (clientIds.Count == 0)
                {
                    _logger.LogInformation("AccountCleanupService > RunAsync started > No client to process");
                    return;
                }
                else
                {
                    _logger.LogInformation("AccountCleanupService > RunAsync started > Processing {Count} clients", clientIds.Count);
                }

                // 2) Cleanup each client in its own context + transaction
                foreach (var clientId in clientIds)
                {
                    _logger.LogInformation("AccountCleanupService > RunAsync started > processing clientId {clientId}", clientId);
                    ct.ThrowIfCancellationRequested();

                    await using var db = await _dbFactory.CreateDbContextAsync(ct);
                    await using var tx = await db.Database.BeginTransactionAsync(ct);

                    try
                    {
                        await CleanupClientDataAsync(db, clientId, ct);

                        // Mark as completed
                        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
                        if (client != null)
                        {
                            client.CleanupCompletedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(ct);
                        }

                        await tx.CommitAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Account cleanup cancelled.");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Rollback happens automatically on dispose if not committed,
                        // but explicit rollback is fine too.
                        _logger.LogError(ex, "Error cleaning up ClientId={ClientId}", clientId);
                        try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Account cleanup job cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running account cleanup job.");
            }
            _logger.LogInformation("✅ AccountCleanupService > RunAsync done");
        }

        private static async Task CleanupClientDataAsync(SpydomoContext db, int clientId, CancellationToken ct)
        {
            // 1) Get all groups for this client
            var groupIds = await db.CompanyGroups
                .AsNoTracking()
                .Where(g => g.ClientId == clientId)
                .Select(g => g.Id)
                .ToListAsync(ct);

            if (groupIds.Count > 0)
            {
                await db.GroupSnapshots
                    .Where(s => groupIds.Contains(s.GroupId))
                    .ExecuteDeleteAsync(ct);

                await db.StrategicSummaries
                    .Where(s => groupIds.Contains(s.CompanyGroupId))
                    .ExecuteDeleteAsync(ct);

                await db.StrategicSignalCache
                    .Where(c => c.GroupId != null && groupIds.Contains(c.GroupId.Value))
                    .ExecuteDeleteAsync(ct);

                await db.TrackedCompanyGroups
                    .Where(tcg => groupIds.Contains(tcg.CompanyGroupId))
                    .ExecuteDeleteAsync(ct);

                await db.CompanyGroups
                    .Where(g => g.ClientId == clientId)
                    .ExecuteDeleteAsync(ct);
            }

            await db.TrackedCompanies
                .Where(tc => tc.ClientId == clientId)
                .ExecuteDeleteAsync(ct);

            await db.Users
                .Where(u => u.ClientId == clientId)
                .ExecuteDeleteAsync(ct);

            // ExecuteDeleteAsync executes immediately; the transaction is what makes it atomic.
        }
    }
}
