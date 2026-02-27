using Spydomo.Infrastructure.ServiceModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISqlRunnerService
    {
        Task<SqlQueryResult> ExecuteSelectAsync(
            string sql,
            int maxRows = 2000,
            int commandTimeoutSeconds = 30,
            CancellationToken ct = default);
    }
}
