using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISignalTypeResolver
    {
        Task<int> GetIdAsync(string slug, CancellationToken ct = default);
        Task<Dictionary<string, int>> GetIdsAsync(IEnumerable<string> slugs, CancellationToken ct = default);
    }
}
