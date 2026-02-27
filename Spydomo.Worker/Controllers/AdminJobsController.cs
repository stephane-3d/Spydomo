using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Spydomo.Infrastructure.AiServices;
using Spydomo.Infrastructure.BackgroundServices;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Worker.Controllers
{
    [ApiController]
    [Route("api/admin/jobs")]
    public class AdminJobsController : ControllerBase
    {
        private readonly CompanyDataService _companyDataService;
        private readonly IInternalContentService _internalContentService;
        private readonly ExternalFeedbackOrchestrator _externalFeedback;
        private readonly ILogger<AdminJobsController> _logger;
        private readonly StrategicSummaryJobService _strategicSummaryJob;
        private readonly IFeedbackDataService _feedback;
        private readonly WarmupService _warmupService;

        public AdminJobsController(
            CompanyDataService companyDataService,
            IInternalContentService internalContentService,
            ExternalFeedbackOrchestrator externalFeedback,
            ILogger<AdminJobsController> logger,
            StrategicSummaryJobService strategicSummaryJob,
            IFeedbackDataService feedback,
            WarmupService warmupService)
        {
            _companyDataService = companyDataService;
            _internalContentService = internalContentService;
            _externalFeedback = externalFeedback;
            _logger = logger;
            _strategicSummaryJob = strategicSummaryJob;
            _feedback = feedback;
            _warmupService = warmupService;
        }

        // POST /api/admin/jobs/companydata/process?companyId=123&mode=inline|hangfire
        [HttpPost("companydata/process")]
        public async Task<IActionResult> ProcessCompanyData(
            [FromQuery] int companyId,
            [FromQuery] string mode = "inline",
            CancellationToken ct = default)
        {
            if (companyId <= 0) return BadRequest("Invalid companyId");

            _logger.LogInformation("Admin job request: companydata companyId={CompanyId} mode={Mode}", companyId, mode);

            if (string.Equals(mode, "hangfire", StringComparison.OrdinalIgnoreCase))
            {
                BackgroundJob.Enqueue(() => _companyDataService.ProcessJobAsync(companyId, CancellationToken.None));
                return Ok(new { ok = true, queued = true });
            }

            await _companyDataService.ProcessJobAsync(companyId, ct);
            return Ok(new { ok = true, queued = false });
        }

        // POST /api/admin/jobs/internalcontent/process?companyId=123&mode=inline|hangfire
        [HttpPost("internalcontent/process")]
        public async Task<IActionResult> ProcessInternalContent(
            [FromQuery] int companyId,
            [FromQuery] string mode = "inline",
            CancellationToken ct = default)
        {
            if (companyId <= 0) return BadRequest("Invalid companyId");

            _logger.LogInformation("Admin job request: internalcontent companyId={CompanyId} mode={Mode}", companyId, mode);

            if (string.Equals(mode, "hangfire", StringComparison.OrdinalIgnoreCase))
            {
                BackgroundJob.Enqueue<IInternalContentService>(svc =>
                    svc.FetchInternalContentForCompanyAsync(companyId));
                return Ok(new { ok = true, queued = true });
            }

            await _internalContentService.FetchInternalContentForCompanyAsync(companyId);
            return Ok(new { ok = true, queued = false });
        }

        // POST /api/admin/jobs/feedback/process?companyId=123&mode=inline|hangfire&force=true|false
        [HttpPost("feedback/process")]
        public async Task<IActionResult> ProcessFeedback(
            [FromQuery] int companyId,
            [FromQuery] string mode = "inline",
            [FromQuery] bool force = false,
            CancellationToken ct = default)
        {
            if (companyId <= 0) return BadRequest("Invalid companyId");

            _logger.LogInformation(
                "Admin job request: feedback companyId={CompanyId} mode={Mode} force={Force}",
                companyId, mode, force);

            if (string.Equals(mode, "hangfire", StringComparison.OrdinalIgnoreCase))
            {
                if (force)
                    BackgroundJob.Enqueue<ExternalFeedbackOrchestrator>(job => job.RunForCompanyForceAsync(companyId));
                else
                    BackgroundJob.Enqueue<ExternalFeedbackOrchestrator>(job => job.RunForCompanyAsync(companyId));

                return Ok(new { ok = true, queued = true, force });
            }

            // inline (for debugging you can still pass ct through to the real method)
            if (force)
                await _externalFeedback.RunForCompanyAsync(companyId, force: true, ct: ct);
            else
                await _externalFeedback.RunForCompanyAsync(companyId, force: false, ct: ct);

            return Ok(new { ok = true, queued = false, force });
        }

        // POST /api/admin/jobs/strategicsummaries/process?companyId=123&mode=inline|hangfire
        [HttpPost("strategicsummaries/process")]
        public async Task<IActionResult> ProcessStrategicSummaries(
            [FromQuery] int companyId,
            [FromQuery] string mode = "inline",
            CancellationToken ct = default)
        {
            if (companyId <= 0) return BadRequest("Invalid companyId");

            _logger.LogInformation("Admin job request: strategicsummaries companyId={CompanyId} mode={Mode}", companyId, mode);

            if (string.Equals(mode, "hangfire", StringComparison.OrdinalIgnoreCase))
            {
                BackgroundJob.Enqueue<StrategicSummaryJobService>(svc =>
                    svc.RunForCompanyAsync(companyId, CancellationToken.None));
                return Ok(new { ok = true, queued = true });
            }

            await _strategicSummaryJob.RunForCompanyAsync(companyId, ct);
            return Ok(new { ok = true, queued = false });
        }

        // POST /api/admin/jobs/reddit/fetch?companyId=123&mode=inline|hangfire
        [HttpPost("reddit/fetch")]
        public async Task<IActionResult> FetchRedditMentions(
            [FromQuery] int companyId,
            [FromQuery] string mode = "inline",
            CancellationToken ct = default)
        {
            if (companyId <= 0) return BadRequest("Invalid companyId");

            _logger.LogInformation("Admin job request: reddit companyId={CompanyId} mode={Mode}", companyId, mode);

            if (string.Equals(mode, "hangfire", StringComparison.OrdinalIgnoreCase))
            {
                BackgroundJob.Enqueue(() => _feedback.FetchRedditMentionsForCompany(companyId));
                return Ok(new { ok = true, queued = true });
            }

            await _feedback.FetchRedditMentionsForCompany(companyId);
            return Ok(new { ok = true, queued = false });
        }

        // POST /api/admin/jobs/warmup/generate?clientId=1&companyId=2&mode=inline|hangfire
        [HttpPost("warmup/generate")]
        public async Task<IActionResult> GenerateWarmup(
            [FromQuery] int clientId,
            [FromQuery] int companyId,
            [FromQuery] string mode = "hangfire",
            CancellationToken ct = default)
        {
            if (clientId <= 0 || companyId <= 0) return BadRequest("Invalid ids");

            _logger.LogInformation("Admin job request: warmup clientId={ClientId} companyId={CompanyId} mode={Mode}",
                clientId, companyId, mode);

            if (string.Equals(mode, "hangfire", StringComparison.OrdinalIgnoreCase))
            {
                BackgroundJob.Enqueue<WarmupService>(svc => svc.GenerateWarmupHangfireAsync(clientId, companyId));
                return Ok(new { ok = true, queued = true });
            }

            await _warmupService.GenerateWarmupAsync(clientId, companyId, ct);
            return Ok(new { ok = true, queued = false });
        }

    }

}
