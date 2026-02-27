using Microsoft.Extensions.Configuration;
using Spydomo.Infrastructure.Interfaces;
using System.Net.Http.Json;

namespace Spydomo.Infrastructure
{
    public sealed class SlackNotifier : ISlackNotifier
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _cfg;

        public SlackNotifier(HttpClient http, IConfiguration cfg)
        {
            _http = http;
            _cfg = cfg;
        }

        public async Task NotifyAsync(string text, CancellationToken ct = default)
        {
            var url = _cfg["Slack:WebhookUrl"];
            if (string.IsNullOrWhiteSpace(url)) return;

            var payload = new { text };
            using var res = await _http.PostAsJsonAsync(url, payload, ct);
            res.EnsureSuccessStatusCode();
        }
    }
}
