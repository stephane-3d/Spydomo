using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure.PulseRules
{
    public static class DedupePolicy
    {
        public static int MinGapDays(string type, PulseRulesOptions opt) => type switch
        {
            "Pain" => opt.DedupeMinGapDaysPain,
            "FeatureRequest" => opt.DedupeMinGapDaysFeature,
            "Praise" => opt.DedupeMinGapDaysPraise,
            _ => 3
        };

        public static async Task<bool> ShouldEmitAsync(
            IPulseObservationRepository repo,
            int companyId,
            string type,
            string topicKey,
            DateTime nowUtc,
            PulseRulesOptions opt,
            CancellationToken ct = default)
        {
            // Cool-down check
            var last = await repo.GetLastNotifiedAtAsync(companyId, type, topicKey, ct).ConfigureAwait(false);
            var minDays = MinGapDays(type, opt);
            var withinCooldown = last is DateTime t && (nowUtc - t).TotalDays < minDays;

            if (!withinCooldown) return true;

            // Surge override: if lots of mentions in recent windows
            var cnt2d = await repo.CountSinceAsync(companyId, type, topicKey, nowUtc.AddDays(-2), ct).ConfigureAwait(false);
            if (cnt2d >= opt.SurgeThreshold2d) return true;

            var cnt7d = await repo.CountSinceAsync(companyId, type, topicKey, nowUtc.AddDays(-7), ct).ConfigureAwait(false);
            if (cnt7d >= opt.SurgeThreshold7d) return true;

            return false; // respect cooldown
        }
    }
}
