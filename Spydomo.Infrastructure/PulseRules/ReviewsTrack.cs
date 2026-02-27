using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Infrastructure.ServiceModels;
using Spydomo.Models;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Spydomo.Infrastructure.PulseRules
{
    public sealed class ReviewsTrack : ITrackProcessor
    {
        private readonly IReadOnlyList<IReviewsRule> _rules;

        public string Name => "Reviews";
        public PulseBucket Bucket => PulseBucket.CustomerVoice;

        public ReviewsTrack(IEnumerable<IReviewsRule> rules)
        {
            // Order rules using RuleMetaAttribute.Order if present
            _rules = rules
                .OrderBy(r => r.GetType().GetCustomAttribute<RuleMetaAttribute>()?.Order ?? 100)
                .ToList();
        }

        public TrackContext BuildContext(int groupId, IEnumerable<SummarizedInfo> sis)
        {
            // Build baselines only from review sources for speed
            var now = DateTime.UtcNow;
            var reviewSis = sis.Where(si => PulseUtils.IsReviewSource(si.SourceTypeEnum));
            var provider = new BaselineProvider(reviewSis, now); // uses 30d for reviews by default
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
                // Cheap prefilter
                var reviewSis = sisLocal.Where(si => PulseUtils.IsReviewSource(si.SourceTypeEnum));

                foreach (var si in reviewSis)
                {
                    token.ThrowIfCancellationRequested();

                    foreach (var rule in _rules)
                    {
                        // Optional metadata routing
                        var meta = rule.GetType().GetCustomAttribute<RuleMetaAttribute>();

                        if (meta?.AppliesToSource is DataSourceTypeEnum src
                            && si.SourceTypeEnum != src)
                        {
                            continue;
                        }

                        if (!rule.IsMatch(si, ctxLocal)) continue;

                        // ASYNC: call the rule
                        var point = await rule.ProjectAsync(si, ctxLocal, token).ConfigureAwait(false);
                        if (point is not null)
                        {
                            // Ensure bucket is set to CustomerVoice (defensive)
                            yield return point with { Bucket = PulseBucket.CustomerVoice };
                        }
                    }
                }
            }
        }
    }
}
