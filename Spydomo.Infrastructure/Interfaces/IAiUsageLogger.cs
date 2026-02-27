using System.Text.Json;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IAiUsageLogger
    {
        Task LogAsync(JsonDocument openAiResponse, string purpose, int? companyId = null, string? prompt = null);

        Task LogAsync(JsonElement openAiResponseElement, string purpose, int? companyId = null, string? prompt = null);

    }
}
