using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICompanyRelationsReconciliationService
    {
        Task ReconcileAsync(int companyId, CancellationToken ct = default);
    }
}
