namespace Spydomo.Common.Enums
{
    public enum SummarizedInfoProcessingStatus
    {
        New = 0,                  // Not processed yet
        GistReady = 1,            // Unified AI summary complete, ready for non-AI steps like mentions
        MentionsDetected = 6,     // Mentions linked, if applicable
        Complete = 7,            // All processing complete, ready for user-facing display
        Error = 99                // Failed during processing
    }
}
