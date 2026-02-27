namespace Spydomo.Models
{
    public class AiUsageLog
    {
        public int Id { get; set; }
        public string Model { get; set; } = "";
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public double CostUsd { get; set; }
        public string Purpose { get; set; } = "";
        public int? CompanyId { get; set; }
        public string? Prompt { get; set; } // Optional prompt text
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
