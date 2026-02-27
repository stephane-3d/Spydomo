using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Spydomo.Infrastructure.PulseRules
{
    public sealed class CommunityTrack : ITrackProcessor
    {
        private readonly IReadOnlyList<ICommunityRule> _rules;

        public string Name => "Community";
        public PulseBucket Bucket => PulseBucket.CustomerVoice;

        public CommunityTrack(IEnumerable<ICommunityRule> rules)
        {
            _rules = rules
                .OrderBy(r => r.GetType().GetCustomAttribute<RuleMetaAttribute>()?.Order ?? 100)
                .ToList();
        }

        public TrackContext BuildContext(int groupId, IEnumerable<SummarizedInfo> sis)
        {
            var now = DateTime.UtcNow;
            var provider = new BaselineProvider(
                sis.Where(si => PulseUtils.IsCommunitySource(si.SourceTypeEnum)),
                now);
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
                var communitySis = sisLocal.Where(si => PulseUtils.IsCommunitySource(si.SourceTypeEnum));

                foreach (var si in communitySis)
                {
                    token.ThrowIfCancellationRequested();

                    foreach (var rule in _rules)
                    {
                        var meta = rule.GetType().GetCustomAttribute<RuleMetaAttribute>();

                        if (meta?.AppliesToSource is DataSourceTypeEnum src
                            && si.SourceTypeEnum != src)
                        {
                            continue;
                        }

                        if (!rule.IsMatch(si, ctxLocal)) continue;

                        var point = await rule.ProjectAsync(si, ctxLocal, token).ConfigureAwait(false);
                        if (point is not null)
                        {
                            // Defensive: enforce CustomerVoice bucket
                            yield return point with { Bucket = PulseBucket.CustomerVoice };
                        }
                    }
                }
            }
        }
    }
}
