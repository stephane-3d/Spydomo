using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Spydomo.Models
{
    [Index(nameof(Hash), IsUnique = true)]
    public class SemanticSignal
    {
        [Key] public long Id { get; set; }

        public int CompanyId { get; set; }
        [Required, MaxLength(64)] public string SourceType { get; set; } = "";  // "G2","Capterra","Reddit","LinkedIn","Blog",...

        public int? RawContentId { get; set; }
        public int? SummarizedInfoId { get; set; }

        public DateTime SeenAt { get; set; }

        [Required, MaxLength(8)] public string Lang { get; set; } = "und";

        [Required, MaxLength(64)] public string Classifier { get; set; } = "llm-v1"; // "llm-v1","embed-v1",...

        [Required] public string IntentsJson { get; set; } = "[]";   // JSON string: [{name,confidence}]
        public string? KeywordsJson { get; set; }                    // JSON string: ["pricing","slow",...]

        // For MSSQL, store embeddings as VARBINARY(MAX)
        public byte[]? Embedding { get; set; }

        public double? ModelScore { get; set; }

        [Required, MaxLength(128)] public string Hash { get; set; } = ""; // sha256 over canonical text + source

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Company Company { get; set; } = null!;
    }
}
