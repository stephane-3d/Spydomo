using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Common.Constants
{
    /// <summary>
    /// Stable identifiers for SignalTypes (matches dbo.SignalTypes.Slug).
    /// Use these in code instead of enums or DB IDs.
    /// </summary>
    public static class SignalSlugs
    {
        public const string StrategicMove = "strategic-move";
        public const string PositioningPlay = "positioning-play";
        public const string FeatureLaunch = "feature-launch";
        public const string PainSignal = "pain-signal";
        public const string FeatureGap = "feature-gap";
        public const string RoiValueProof = "roi-value-proof";
        public const string CompetitiveMention = "competitive-mention";
        public const string ConversionAngle = "conversion-angle";
        public const string DiscoverySignal = "discovery-signal";
        public const string GrowthSignal = "growth-signal";
        public const string PricingSignal = "pricing-signal";
        public const string RetentionSignal = "retention-signal";

        // Derived / rules signals
        public const string MarketingTactic = "marketing-tactic";
        public const string SocialProofDrop = "social-proof-drop";
        public const string ConversionPlay = "conversion-play";
        public const string SentimentShift = "sentiment-shift";
        public const string EngagementSpike = "engagement-spike";
        public const string EmergingTheme = "emerging-theme";
    }
}
