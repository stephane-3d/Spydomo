using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spydomo.Infrastructure
{
    public sealed class SqlRunnerService : ISqlRunnerService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<SqlRunnerService> _logger;

        public SqlRunnerService(
            IDbContextFactory<SpydomoContext> dbFactory,
            ILogger<SqlRunnerService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task<SqlQueryResult> ExecuteSelectAsync(
            string sql,
            int maxRows = 2000,
            int commandTimeoutSeconds = 30,
            CancellationToken ct = default)
        {
            try
            {
                sql = (sql ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(sql))
                    return Fail("SQL is empty.");

                if (!LooksLikeSelectOnly(sql, out var reason))
                    return Fail($"Blocked. {reason}");

                if (maxRows < 1) maxRows = 1;
                if (maxRows > 100_000) maxRows = 100_000; // hard cap

                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var conn = db.Database.GetDbConnection();

                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync(ct);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandType = CommandType.Text;
                cmd.CommandTimeout = commandTimeoutSeconds;

                // Optional: reduce lock contention; keep commented unless you really want it.
                // cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; " + sql;

                var sw = System.Diagnostics.Stopwatch.StartNew();

                await using var reader = await cmd.ExecuteReaderAsync(ct);

                var cols = new List<string>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                    cols.Add(reader.GetName(i));

                var rows = new List<Dictionary<string, object?>>();
                int total = 0;
                int returned = 0;

                while (await reader.ReadAsync(ct))
                {
                    total++;

                    if (returned < maxRows)
                    {
                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            object? value = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);

                            // Normalize a few common non-JSON-friendly types
                            value = Normalize(value);

                            row[cols[i]] = value;
                        }

                        rows.Add(row);
                        returned++;
                    }
                }

                sw.Stop();

                return new SqlQueryResult(
                    Success: true,
                    Error: null,
                    RowCount: total,
                    TruncatedToMaxRows: returned,
                    ElapsedMs: sw.ElapsedMilliseconds,
                    Columns: cols,
                    Rows: rows
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL runner error");
                return Fail(ex.Message);
            }

            SqlQueryResult Fail(string error) =>
                new(false, error, 0, 0, 0, Array.Empty<string>(), Array.Empty<Dictionary<string, object?>>());
        }

        private static object? Normalize(object? value)
        {
            if (value is null) return null;

            // SQL Server sometimes returns these
            if (value is DateTime dt) return dt; // JsonSerializer will output ISO
            if (value is DateTimeOffset dto) return dto;
            if (value is Guid g) return g.ToString();

            // byte[] becomes base64 in JSON by default if you leave it as byte[].
            // If you prefer base64 string explicitly:
            if (value is byte[] bytes) return Convert.ToBase64String(bytes);

            if (value is string s)
            {
                var t = s.Trim();
                if ((t.StartsWith("{") && t.EndsWith("}")) || (t.StartsWith("[") && t.EndsWith("]")))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(t);
                        return doc.RootElement.Clone();
                    }
                    catch { /* keep as string */ }
                }
                return s;
            }


            // Decimal is fine (JSON number)
            return value;
        }

        /// <summary>
        /// Guardrail: allow only SELECT / WITH…SELECT (CTE) and block known write/exec keywords.
        /// This is not a full SQL parser, but it prevents accidents.
        /// </summary>
        private static bool LooksLikeSelectOnly(string sql, out string reason)
        {
            reason = "Only SELECT queries are allowed.";

            // Strip leading comments (simple cases)
            sql = Regex.Replace(sql, @"^\s*(--[^\r\n]*\r?\n|\s*/\*.*?\*/\s*)+", "", RegexOptions.Singleline).TrimStart();

            // Must start with SELECT or WITH
            bool okStart = sql.StartsWith("select", StringComparison.OrdinalIgnoreCase)
                           || sql.StartsWith("with", StringComparison.OrdinalIgnoreCase);

            if (!okStart) { reason = "Query must start with SELECT or WITH."; return false; }

            // Block obvious write/exec/danger tokens anywhere
            var blocked = new[]
            {
                "insert", "update", "delete", "merge", "drop", "alter", "create", "truncate",
                "exec", "execute", "sp_", "xp_", "grant", "revoke", "deny",
                "backup", "restore"
            };

            foreach (var token in blocked)
            {
                if (Regex.IsMatch(sql, $@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase))
                {
                    reason = $"Blocked keyword detected: {token}";
                    return false;
                }
            }

            // Optional: block multiple statements
            if (sql.Contains(';'))
            {
                reason = "Multiple statements are blocked (semicolon detected).";
                return false;
            }

            return true;
        }
    }
}
