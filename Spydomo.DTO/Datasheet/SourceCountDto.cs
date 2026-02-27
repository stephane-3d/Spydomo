namespace Spydomo.DTO.Datasheet
{
    public sealed class SourceCountDto
    {
        public int TypeId { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public string? LinkUrl { get; set; }
    }
}
