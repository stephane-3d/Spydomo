using Microsoft.Playwright;

namespace Spydomo.Infrastructure
{
    public sealed class RemoteBrowser : IAsyncDisposable
    {
        private readonly IPlaywright _pw;
        public IBrowser Browser { get; }

        private RemoteBrowser(IPlaywright pw, IBrowser browser)
        {
            _pw = pw;
            Browser = browser;
        }

        public static async Task<RemoteBrowser> ConnectAsync(string auth, string host = "brd.superproxy.io:9222", int timeoutMs = 30_000)
        {
            var pw = await Playwright.CreateAsync();
            var cdpUrl = $"wss://{auth}@{host}";
            var browser = await pw.Chromium.ConnectOverCDPAsync(cdpUrl, new BrowserTypeConnectOverCDPOptions
            {
                Timeout = timeoutMs
            });
            return new RemoteBrowser(pw, browser);
        }

        public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
            => Browser.NewContextAsync(options ?? new());

        public async ValueTask DisposeAsync()
        {
            try { await Browser.CloseAsync(); } catch { /* ignore */ }
            _pw.Dispose();
        }
    }
}
