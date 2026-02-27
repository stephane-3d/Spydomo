using System;
using System.Collections.Generic;
using System.Text;
using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISignalTypeOptionsProvider
    {
        Task<List<SignalTypeOption>> GetAllowedAsync(bool forceRefresh = false, CancellationToken ct = default);
        void Invalidate();
    }
}
