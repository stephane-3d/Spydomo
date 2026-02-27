using Microsoft.JSInterop;
using System.Text.Json;

namespace Spydomo.Web.Classes
{
    public interface IBrowserStorage
    {
        Task SetSessionAsync<T>(string key, T value);
        Task<T?> GetSessionAsync<T>(string key);
        Task RemoveSessionAsync(string key);
    }

    public sealed class BrowserStorage : IBrowserStorage
    {
        private readonly IJSRuntime _js;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
        public BrowserStorage(IJSRuntime js) => _js = js;

        public Task SetSessionAsync<T>(string key, T value) =>
            _js.InvokeVoidAsync("sessionStorage.setItem", key, JsonSerializer.Serialize(value, JsonOpts)).AsTask();

        public async Task<T?> GetSessionAsync<T>(string key)
        {
            var json = await _js.InvokeAsync<string?>("sessionStorage.getItem", key);
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json!, JsonOpts);
        }

        public Task RemoveSessionAsync(string key) =>
            _js.InvokeVoidAsync("sessionStorage.removeItem", key).AsTask();
    }
}
