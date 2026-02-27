using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Models;
using System.Data;

namespace Spydomo.Infrastructure.BackgroundServices
{
    public class GistJobService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly RawContentProcessor _processor;
        private readonly ILogger<GistJobService> _logger;

        private const int BatchSize = 5;
        private const int MaxPerRun = 10;
        private const int LookbackDays = 30;

        public TimeSpan ProcessingStaleAfter { get; set; } = TimeSpan.FromHours(2);

        public GistJobService(
            RawContentProcessor processor,
            ILogger<GistJobService> logger,
            IDbContextFactory<SpydomoContext> dbFactory)
        {
            _processor = processor;
            _logger = logger;
            _dbFactory = dbFactory;
        }

        public async Task RunAsync(IJobCancellationToken hangfireToken)
        {
            var ct = hangfireToken?.ShutdownToken ?? CancellationToken.None;

            // Try to acquire a distributed lock quickly. If not available, just skip silently.
            using var connection = JobStorage.Current.GetConnection();
            try
            {
                using var @lock = connection.AcquireDistributedLock("GistJobService.RunAsync", TimeSpan.FromSeconds(1));
                await RunInternalAsync(ct);
            }
            catch (DistributedLockTimeoutException)
            {
                // Another run is active.
                return;
            }
        }

        private async Task RunInternalAsync(CancellationToken ct)
        {
            _logger.LogInformation("⏱ GistJobService > Run started");

            try
            {
                var since = DateTime.UtcNow.AddDays(-LookbackDays);
                var processed = 0;

                // 0) Recover stuck PROCESSING rows
                await RecoverStuckProcessingAsync(since, ct);

                while (processed < MaxPerRun)
                {
                    ct.ThrowIfCancellationRequested();

                    List<int> ids;
                    await using (var db = await _dbFactory.CreateDbContextAsync(ct))
                    {
                        ids = await ClaimNextBatchAsync(db, since, BatchSize, ct);
                    }

                    if (ids.Count == 0)
                    {
                        _logger.LogInformation("⏳ No pending gists to process.");
                        return;
                    }

                    _logger.LogInformation("🔄 Processing batch of {Count} RawContents.", ids.Count);

                    try
                    {
                        var okCount = await _processor.ProcessBatchAsync(ids, ct);

                        // Count “attempted” vs “success”
                        processed += ids.Count; // keeps the run bounded even if some fail

                        _logger.LogInformation("✅ Batch done. Success={OkCount}/{BatchCount}", okCount, ids.Count);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        _logger.LogInformation("🛑 Gist job cancelled.");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing batch of {Count} RawContents.", ids.Count);

                        // Best effort: revert claimed batch so it doesn’t stay PROCESSING forever.
                        // (RecoverStuckProcessingAsync is a safety net, but this keeps things moving.)
                        await MarkBatchAsync(ids, RawContentStatusEnum.NEW, clearProcessingAt: true, ct);

                        // Still count the attempt to avoid infinite loops
                        processed += ids.Count;
                    }
                }

                _logger.LogInformation("✅ Gist run completed. Attempted {Count} RawContents.", processed);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 Gist job cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔥 Critical error in Gist job run.");
                throw;
            }
            finally
            {
                _logger.LogInformation("✅ GistJobService > Run ended");
            }
        }

        private async Task MarkBatchAsync(List<int> ids, string status, bool clearProcessingAt, CancellationToken ct)
        {
            if (ids == null || ids.Count == 0) return;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Efficient set-based update
            if (clearProcessingAt)
            {
                await db.RawContents
                    .Where(r => ids.Contains(r.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, status)
                        .SetProperty(x => x.ProcessingAt, (DateTime?)null),
                        ct);
            }
            else
            {
                await db.RawContents
                    .Where(r => ids.Contains(r.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, status),
                        ct);
            }
        }

        private async Task RecoverStuckProcessingAsync(DateTime since, CancellationToken ct)
        {
            var staleBefore = DateTime.UtcNow.Subtract(ProcessingStaleAfter);

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var sql = @"
UPDATE dbo.RawContents
SET Status = @new,
    ProcessingAt = NULL
WHERE Status = @processing
  AND ProcessingAt IS NOT NULL
  AND ProcessingAt < @staleBefore
  AND ISNULL(PostedDate, CreatedAt) >= @since;
";

                var affected = await db.Database.ExecuteSqlRawAsync(
                    sql,
                    new Microsoft.Data.SqlClient.SqlParameter("@new", RawContentStatusEnum.NEW),
                    new Microsoft.Data.SqlClient.SqlParameter("@processing", RawContentStatusEnum.PROCESSING),
                    new Microsoft.Data.SqlClient.SqlParameter("@staleBefore", staleBefore),
                    new Microsoft.Data.SqlClient.SqlParameter("@since", since));

                if (affected > 0)
                    _logger.LogWarning("🧹 Recovered {Count} stuck RawContents from PROCESSING -> NEW.", affected);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️ Failed to recover stuck rows: {Err}", ex.ToString());
            }
        }

        private async Task<List<int>> ClaimNextBatchAsync(
            SpydomoContext db,
            DateTime since,
            int batchSize,
            CancellationToken ct)
        {
            var sql = @"
DECLARE @companyId INT;

SELECT TOP (1) @companyId = x.CompanyId
FROM (
    SELECT rc.CompanyId, MIN(rc.Id) AS MinId
    FROM dbo.RawContents rc WITH (READPAST)
    WHERE rc.Status = @new
      AND rc.CompanyId IS NOT NULL
      AND rc.Content IS NOT NULL
      AND (
            rc.PostedDate >= @since
         OR (rc.PostedDate IS NULL AND rc.CreatedAt >= @since)
      )
    GROUP BY rc.CompanyId
) x
ORDER BY x.MinId;

IF @companyId IS NULL
BEGIN
    SELECT CAST(NULL AS INT) WHERE 1 = 0;
    RETURN;
END

;WITH cte AS (
    SELECT TOP (@batchSize) rc.Id
    FROM dbo.RawContents rc WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE rc.Status = @new
      AND rc.CompanyId = @companyId
      AND rc.Content IS NOT NULL
      AND (
      rc.PostedDate >= @since
       OR (rc.PostedDate IS NULL AND rc.CreatedAt >= @since)
    )
    ORDER BY rc.Id
)
UPDATE rc
SET rc.Status = @processing,
    rc.ProcessingAt = SYSUTCDATETIME()
OUTPUT inserted.Id
FROM dbo.RawContents rc
JOIN cte ON cte.Id = rc.Id;
";

            var ids = new List<int>(batchSize);

            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await db.Database.OpenConnectionAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 120; // seconds

            AddParam(cmd, "@batchSize", batchSize);
            AddParam(cmd, "@new", RawContentStatusEnum.NEW);
            AddParam(cmd, "@processing", RawContentStatusEnum.PROCESSING);
            AddParam(cmd, "@since", since);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                ids.Add(reader.GetInt32(0));

            return ids;
        }

        private static void AddParam(IDbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}