namespace Spydomo.Models
{
    public class PulseTopicState
    {
        public long Id { get; set; }
        public int CompanyId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string TopicKey { get; set; } = string.Empty;

        public DateTime? LastNotifiedAt { get; set; }  // when we last emitted a pulse
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Company? Company { get; set; }
    }
}
