namespace Spydomo.DTO
{
    public sealed class SourceBreakdown
    {
        public int Version { get; set; } = 2;
        public Dictionary<string, SourceEntry> Sources { get; set; } = new();
    }
}
