using Microsoft.JSInterop;
using MudBlazor;

namespace Spydomo.Web.Classes.Extensions
{
    public static class JsRuntimeClipboardExtensions
    {
        public static async Task<bool> CopyToClipboardAsync(this IJSRuntime js, string text, ISnackbar? snackbar = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
                snackbar?.Add("Copied", Severity.Success, c => c.VisibleStateDuration = 1500);
                return true;
            }
            catch
            {
                var ok = await js.InvokeAsync<bool>("spydomoCopyText", text);
                snackbar?.Add(ok ? "Copied" : "Copy failed", ok ? Severity.Success : Severity.Error,
                              c => c.VisibleStateDuration = 1500);
                return ok;
            }
        }
    }

}
