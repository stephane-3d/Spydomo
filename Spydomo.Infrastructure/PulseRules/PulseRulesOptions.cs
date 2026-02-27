namespace Spydomo.Infrastructure.PulseRules
{
    public sealed class PulseRulesOptions
    {
        public int LowVolumeReviewThresholdPerMonth { get; set; } = 3;

        // Reviews
        public int RedditTier2MinComments { get; set; } = 10;
        public double FeatureRequestMinConfidence { get; set; } = 0.70;
        public int FeatureRequestTier1MinCount14d { get; set; } = 3;
        public double LowStarThreshold { get; set; } = 2.0;
        public bool HeadlinePreemptsObservation { get; set; } = true;

        // Content patterns
        public int ThemeSurgeMinPosts14d { get; set; } = 5;
        public double ThemeSurgeZScore { get; set; } = 2.0;

        // Dedupe / backoff
        public int DedupeMinGapDaysPain { get; set; } = 2;           // 1 pulse per 2 days per topic
        public int DedupeMinGapDaysFeature { get; set; } = 3;        // 1 per 3 days
        public int DedupeMinGapDaysPraise { get; set; } = 7;         // 1 per 7 days

        public int SurgeThreshold2d { get; set; } = 3;               // if >=3 mentions in 48h, allow earlier pulse
        public int SurgeThreshold7d { get; set; } = 6;               // or >=6 mentions in 7d
    }
}
