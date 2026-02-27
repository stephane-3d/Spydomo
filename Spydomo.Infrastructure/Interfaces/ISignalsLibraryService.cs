using Spydomo.DTO.SignalLibrary;
using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ISignalsLibraryService
    {
        Task<List<SignalsIndexRow>> GetCategoriesIndexAsync(DateTime utcNow, CancellationToken ct);
        Task<(string CategoryName, List<SignalTypeRow> SignalTypes)> GetCategoryAsync(string categorySlug, DateTime utcNow, CancellationToken ct);
        Task<(string CategoryName, string SignalTypeName, string SignalTypeDescription, List<ThemeRow> Themes)> GetCategorySignalAsync(string categorySlug, string signalTypeSlug, DateTime utcNow, CancellationToken ct);
        Task<ThemeDetailVm?> GetThemeDetailAsync(string categorySlug, string signalTypeSlug, string themeSlug, DateTime utcNow, CancellationToken ct);
    }
}
