using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.ServiceModels
{
    public sealed record SqlQueryResult(
        bool Success,
        string? Error,
        int RowCount,
        int TruncatedToMaxRows,
        long ElapsedMs,
        IReadOnlyList<string> Columns,
        IReadOnlyList<Dictionary<string, object?>> Rows
    );
}
