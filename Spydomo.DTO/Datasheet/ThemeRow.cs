namespace Spydomo.DTO.Datasheet
{
    public sealed class ThemeRow
    {
        public int? CanonicalThemeId { get; set; }   // may be null
        public string Label { get; set; } = "";      // pretty label to display
        public string Reason { get; set; } = "";     // representative (latest) reason
        public int Count { get; set; }               // occurrences in period
        public DateTime UpdatedAt { get; set; }      // most recent for this theme
    }
}
