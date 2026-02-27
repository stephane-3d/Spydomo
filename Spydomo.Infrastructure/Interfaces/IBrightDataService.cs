using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IBrightDataService
    {
        /// <summary>
        /// Fetches HTML/text for a URL. Implementation can use proxy GET, Unlocker POST, or a fallback strategy.
        /// Returns null when content could not be fetched or is empty.
        /// </summary>
        Task<string?> FetchHtmlAsync(string url, CancellationToken ct = default);

        /// <summary>
        /// Optional: force a specific transport (useful for debugging / hard sites).
        /// </summary>
        Task<string?> FetchHtmlAsync(string url, BrightDataFetchMode mode, CancellationToken ct = default);

        // --- Existing dataset APIs (keep as-is) ---
        Task<string> TriggerScrapingAsync(
            string datasetId,
            string url,
            int pages = 10,
            string sortFilter = "Most Recent");

        Task<string> TriggerDiscoveryScrapingAsync(string datasetId, string keyword, string? fromDate = null);

        Task<string> TriggerUrlScrapingAsync(
            string datasetId,
            List<Dictionary<string, string>> payloadItems,
            Dictionary<string, string>? extraQueryParams = null);

        Task<string?> DownloadSnapshotAsync(string snapshotId, CancellationToken ct = default);

        Task<RemoteBrowser> ConnectToRemoteBrowserAsync(CancellationToken ct = default);

        Task<List<string>> SearchPlatformsAsync(int companyId, CancellationToken ct = default);

        Task<string?> FetchJsonAsync(string targetUrl, CancellationToken ct = default);

        Task<string?> FetchRedditJsonAsync(string targetUrl, CancellationToken ct = default);
    }
}
