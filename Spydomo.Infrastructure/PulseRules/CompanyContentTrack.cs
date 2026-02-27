using Microsoft.Extensions.Logging;
using Spydomo.Common.Enums;
using Spydomo.Infrastructure.BackgroundServices;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.PulseRules.CompanyContent;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Spydomo.Infrastructure.PulseRules
{
    public sealed class CompanyContentTrack : ITrackProcessor
    {
        private readonly IReadOnlyList<ICompanyContentRule> _rules;
        private readonly ILogger<StrategicSummaryService> _logger;

        public string Name => "CompanyContent";
        public PulseBucket Bucket => PulseBucket.Marketing;

        public CompanyContentTrack(IEnumerable<ICompanyContentRule> rules, ILogger<StrategicSummaryService> logger)
        {
            _rules = rules
                .OrderBy(r => r.GetType().GetCustomAttribute<RuleMetaAttribute>()?.Order ?? 100)
                .ToList();
            _logger = logger;
        }

        public TrackContext BuildContext(int groupId, IEnumerable<SummarizedInfo> sis)
        {
            var now = DateTime.UtcNow;
            var contentSis = sis.Where(si => PulseUtils.IsContentSource(si.SourceTypeEnum));
            var provider = new BaselineProvider(contentSis, now); // supports ThemeSurge/ChannelMix
            return new TrackContext(groupId, provider, now);
        }

        public IAsyncEnumerable<PulsePoint> EvaluateAsync(
    IEnumerable<SummarizedInfo> sis,
    TrackContext ctx,
    CancellationToken ct = default)
        {
            return EvaluateCore(sis, ctx, ct);

            async IAsyncEnumerable<PulsePoint> EvaluateCore(
                IEnumerable<SummarizedInfo> sisLocal,
                TrackContext ctxLocal,
                [EnumeratorCancellation] CancellationToken token)
            {
                var runId = Guid.NewGuid().ToString("N")[..8];

                var contentSis = sisLocal
                    .Where(si => PulseUtils.IsContentSource(si.SourceTypeEnum))
                    .ToList(); // materialize once

                var distinctIds = contentSis.Select(x => x.Id).Distinct().Count();
                var distinctRaw = contentSis.Select(x => x.RawContentId).Distinct().Count();

                _logger.LogInformation(
                    "CompanyContentTrack[{RunId}] START groupId={GroupId} sisIn={SisIn} contentSis={ContentSis} distinctSiIds={DistinctIds} distinctRawIds={DistinctRaw} rules={Rules}",
                    runId, ctxLocal.GroupId, sisLocal.Count(), contentSis.Count, distinctIds, distinctRaw, _rules.Count);

                // Detect duplicates immediately
                var dupes = contentSis
                    .GroupBy(x => x.Id)
                    .Where(g => g.Count() > 1)
                    .Select(g => new { Id = g.Key, Count = g.Count() })
                    .Take(10)
                    .ToList();

                if (dupes.Count > 0)
                    _logger.LogWarning("CompanyContentTrack[{RunId}] DUPLICATE SI IDS detected (first10): {Dupes}",
                        runId, string.Join(", ", dupes.Select(d => $"{d.Id}x{d.Count}")));

                var produced = 0;
                var openAiCalls = 0;

                foreach (var si in contentSis)
                {
                    token.ThrowIfCancellationRequested();

                    _logger.LogDebug("CompanyContentTrack[{RunId}] SI id={SiId} companyId={CompanyId} src={Src} rawId={RawId} date={Date}",
                        runId, si.Id, si.CompanyId, si.SourceTypeEnum, si.RawContentId, si.Date);

                    foreach (var rule in _rules)
                    {
                        var ruleName = rule.GetType().Name;
                        var meta = rule.GetType().GetCustomAttribute<RuleMetaAttribute>();

                        if (meta?.AppliesToSource is DataSourceTypeEnum src && si.SourceTypeEnum != src)
                            continue;

                        if (!rule.IsMatch(si, ctxLocal))
                            continue;

                        _logger.LogDebug("CompanyContentTrack[{RunId}] MATCH rule={Rule} siId={SiId}", runId, ruleName, si.Id);

                        // If this rule is the OpenAI one, count it
                        if (rule is CompanyObservationRule) openAiCalls++;

                        var point = await rule.ProjectAsync(si, ctxLocal, token).ConfigureAwait(false);
                        if (point is not null)
                        {
                            produced++;
                            yield return point with { Bucket = PulseBucket.Marketing };
                        }
                    }
                }

                _logger.LogInformation("CompanyContentTrack[{RunId}] END produced={Produced} openAiRuleInvocations={OpenAiCalls}",
                    runId, produced, openAiCalls);
            }
        }

    }
}

