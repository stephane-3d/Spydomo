using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure;
using Spydomo.Infrastructure.BackgroundServices;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text;
using System.Text.Json;

namespace Spydomo.Web.Controllers
{
    [ApiController]
    [Route("api/webhooks/brightdata")]
    public class BrightDataWebhookController : ControllerBase
    {
        private readonly IFeedbackDataService _feedbackDataService;
        private readonly ISnapshotTrackerService _snapshotTrackerService;
        private readonly IBrightDataService _brightDataService;
        private readonly SpydomoContext _dbContext;
        private readonly DbDataService _dbDataService;
        private readonly CompanyDataService _companyService;
        private readonly ILogger<BrightDataWebhookController> _logger;
        private readonly IFeedbackParserResolver _parserResolver;

        private static readonly HashSet<string> CrawlFailureCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "crawl_failed",
            "dead_page"
            // Add more if needed: "captcha_blocked", "timeout", etc.
        };

        public BrightDataWebhookController(
            IFeedbackDataService feedbackDataService,
            IFeedbackParserResolver parserResolver,
            ISnapshotTrackerService snapshotTrackerService,
            IBrightDataService brightDataService,
            SpydomoContext spydomoContext,
            DbDataService dbDataService,
            CompanyDataService companyService,
            ILogger<BrightDataWebhookController> logger)
        {
            _feedbackDataService = feedbackDataService;
            _parserResolver = parserResolver;
            _snapshotTrackerService = snapshotTrackerService;
            _brightDataService = brightDataService;
            _dbContext = spydomoContext;
            _dbDataService = dbDataService;
            _companyService = companyService;
            _logger = logger;
        }

        [HttpPost("jobdone")]
        public async Task<IActionResult> JobDone(CancellationToken ct)
        {
            Console.WriteLine("🎯 Received BrightData notify webhook");
            string snapshotData = "";
            string jsonResponse = "";

            _logger.LogInformation("Webhook received. ContentLength={ContentLength}, TransferEncoding={TransferEncoding}, ContentType={ContentType}",
                Request.ContentLength,
                Request.Headers["Transfer-Encoding"].ToString(),
                Request.ContentType);

            try
            {
                using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                {
                    jsonResponse = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(jsonResponse))
                {
                    _logger.LogWarning("Empty webhook body. ContentLength={ContentLength}, TransferEncoding={TransferEncoding}, ContentType={ContentType}",
                        Request.ContentLength,
                        Request.Headers["Transfer-Encoding"].ToString(),
                        Request.ContentType);

                    return BadRequest("Empty request body");
                }

                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                if (!root.TryGetProperty("snapshot_id", out var snapshotIdProp))
                {
                    Console.WriteLine("Missing snapshot_id in payload");
                    return BadRequest("Missing snapshot_id");
                }

                string snapshotId = snapshotIdProp.GetString();
                _logger.LogInformation($"Snapshot ID: {snapshotId}");

                var job = await _snapshotTrackerService.GetJobBySnapshotIdAsync(snapshotId);
                if (job == null)
                {
                    _logger.LogWarning($"❌ No matching SnapshotJob found for snapshot ID: {snapshotId}");
                    return Ok("No matching snapshot job");
                }

                int companyId = job.CompanyId;
                var sourceTypeId = job.DataSourceTypeId;
                var originType = job.OriginType;

                snapshotData = await _brightDataService.DownloadSnapshotAsync(snapshotId);
                if (string.IsNullOrEmpty(snapshotData))
                {
                    await _snapshotTrackerService.MarkSnapshotFailedAsync(snapshotId);
                    _logger.LogError($"Snapshot data empty or failed to download for snapshotId: {snapshotId}. jsonResponse: {jsonResponse}");
                    return Ok("Failed to download snapshot data");
                }

                var partition = PartitionSnapshot(snapshotData);

                // ✅ If there are NO valid rows, then this snapshot is genuinely "failed"
                if (partition.ValidCount == 0)
                {
                    await _snapshotTrackerService.MarkSnapshotFailedAsync(snapshotId);

                    // Facebook Reviews special case
                    if (partition.FirstWarningCode == "dead_page" &&
                        sourceTypeId == (int)DataSourceTypeEnum.FacebookReviews)
                    {
                        await _companyService.MarkFacebookReviewsAsUnavailableAsync(companyId);
                        // If "unavailable" means "stop retrying", don't reset date here.
                    }

                    _logger.LogWarning(
                        "Snapshot had no valid rows. Marked failed. snapshotId={SnapshotId}, warning={WarningCode}, error={ErrorCode}, sourceTypeId={SourceTypeId}, errors={ErrorCount}",
                        snapshotId, partition.FirstWarningCode, partition.FirstErrorCode, sourceTypeId, partition.ErrorCount);

                    // ✅ IMPORTANT: This is not a server crash. Acknowledge webhook.
                    return Ok("Snapshot contained no valid rows (marked failed).");
                }

                // ✅ We DO have valid rows, so parse only those
                if (partition.ErrorCount > 0)
                {
                    _logger.LogInformation(
                        "Snapshot contains mixed rows. Processing valid rows only. snapshotId={SnapshotId}, valid={ValidCount}, errors={ErrorCount}, firstWarning={WarningCode}, firstError={ErrorCode}",
                        snapshotId, partition.ValidCount, partition.ErrorCount, partition.FirstWarningCode, partition.FirstErrorCode);
                }

                // Use valid-only JSON for parsing
                snapshotData = partition.ValidJsonArray;

                var company = await _dbContext.Companies.FindAsync(companyId);
                if (company == null)
                {
                    Console.WriteLine($"❌ Company not found for companyId: {companyId}");
                    return (StatusCode(500, "Company not found"));
                }

                var type = (DataSourceTypeEnum)sourceTypeId;
                var parser = _parserResolver.Get(type);

                if (parser == null)
                    throw new Exception($"Unsupported DataSourceTypeId: {sourceTypeId} ({type})");

                // If you have a DataSource row for this source type, pass it (optional but nice)
                var dataSource = company.DataSources.FirstOrDefault(ds => ds.TypeId == sourceTypeId);

                // Decide "lastUpdate" consistently
                DateTime? lastUpdate = type switch
                {
                    DataSourceTypeEnum.Reddit => company.LastRedditLookup,
                    DataSourceTypeEnum.Linkedin => company.LastLinkedinLookup,
                    DataSourceTypeEnum.FacebookReviews => company.LastFacebookReviewsLookup,
                    DataSourceTypeEnum.Facebook => company.LastFacebookLookup,
                    _ => dataSource?.LastUpdate
                };

                // Parse
                var feedbackList = await parser.Parse(
                    snapshotData,
                    companyId,
                    dataSource,     // can be null if your parsers tolerate it (they already do)
                    lastUpdate,
                    originType
                );

                if (feedbackList.Any())
                {
                    await _feedbackDataService.StoreReviewsAsync(companyId, feedbackList);
                }

                if (sourceTypeId == (int)DataSourceTypeEnum.Linkedin)
                {
                    var allUrls = JsonSerializer.Deserialize<List<string>>(job.TrackingData ?? "[]") ?? new();
                    var matchedUrls = feedbackList.Select(f => f.PostUrl.TrimEnd('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var filteredUrls = allUrls
                        .Where(u => !matchedUrls.Contains(u.TrimEnd('/')))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var filteredUrl in filteredUrls)
                    {
                        await _dbDataService.AddFilteredUrlAsync(
                            companyId,
                            filteredUrl,
                            (int)DataSourceTypeEnum.Linkedin,
                            "NotRelevant",
                            ct
                        );
                    }
                }

                if (sourceTypeId == (int)DataSourceTypeEnum.FacebookReviews)
                {
                    company.HasFacebookReviews = true;
                    await _dbContext.SaveChangesAsync();
                }

                if (partition.ErrorCount > 0)
                    await _snapshotTrackerService.MarkSnapshotCompletedWithWarningsAsync(snapshotId);
                else
                    await _snapshotTrackerService.MarkSnapshotCompletedAsync(snapshotId);

                // update the last update date
                var dataSourceUpd = await _dbContext.DataSources
                    .FirstOrDefaultAsync(ds => ds.CompanyId == companyId && ds.TypeId == sourceTypeId);

                if (dataSourceUpd != null)
                {
                    dataSourceUpd.LastUpdate = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation($"✅ DataSource.LastUpdate updated for {company.Name} ({sourceTypeId})");
                }
                else
                {
                    _logger.LogInformation($"⚠️ No matching DataSource found to update LastUpdate for companyId {companyId}, typeId {sourceTypeId}");
                }

                return Ok("Snapshot processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing BrightData webhook. Error: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        private bool SnapshotHasCrawlFailed(string snapshotData)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(snapshotData);
                var root = jsonDoc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        if (element.TryGetProperty("error_code", out var errorCodeProp) &&
                            CrawlFailureCodes.Contains(errorCodeProp.GetString()))
                        {
                            return true;
                        }

                        if (element.TryGetProperty("warning_code", out var warningCodeProp) &&
                            CrawlFailureCodes.Contains(warningCodeProp.GetString()))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine("JSON parse error: " + ex.Message);
            }

            return false;
        }

        private string? GetSnapshotWarningCode(string snapshotData)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(snapshotData);
                var root = jsonDoc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        if (element.TryGetProperty("warning_code", out var warningCodeProp))
                        {
                            return warningCodeProp.GetString();
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine("JSON parse error: " + ex.Message);
            }

            return null;
        }

        private sealed record SnapshotPartition(
            string ValidJsonArray,
            int ValidCount,
            int ErrorCount,
            string? FirstWarningCode,
            string? FirstErrorCode
        );

        private static SnapshotPartition PartitionSnapshot(string snapshotData)
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshotData);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                    return new SnapshotPartition("[]", 0, 1, FirstWarningCode: null, FirstErrorCode: "not_array");

                var valids = new List<string>();
                int errors = 0;

                string? firstWarning = null;
                string? firstError = null;

                foreach (var el in root.EnumerateArray())
                {
                    // BrightData error rows tend to have error_code / warning_code / error
                    var hasErrorCode = el.TryGetProperty("error_code", out var ec);
                    var hasWarningCode = el.TryGetProperty("warning_code", out var wc);
                    var hasError = el.TryGetProperty("error", out _);

                    var isErrorRow = hasErrorCode || hasWarningCode || hasError;

                    if (isErrorRow)
                    {
                        errors++;

                        if (firstWarning == null && hasWarningCode && wc.ValueKind == JsonValueKind.String)
                            firstWarning = wc.GetString();

                        if (firstError == null && hasErrorCode && ec.ValueKind == JsonValueKind.String)
                            firstError = ec.GetString();

                        continue;
                    }

                    valids.Add(el.GetRawText());
                }

                var validJson = valids.Count == 0 ? "[]" : $"[{string.Join(",", valids)}]";
                return new SnapshotPartition(validJson, valids.Count, errors, firstWarning, firstError);
            }
            catch
            {
                // If snapshotData is not JSON or unreadable, treat as all-error
                return new SnapshotPartition("[]", 0, 1, FirstWarningCode: null, FirstErrorCode: "invalid_json");
            }
        }

    }
}
