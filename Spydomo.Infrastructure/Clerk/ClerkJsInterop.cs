using Microsoft.JSInterop;

namespace Spydomo.Infrastructure.Clerk
{
    public class ClerkJsInterop
    {
        private readonly IJSRuntime _js;

        public ClerkJsInterop(IJSRuntime js)
        {
            _js = js;
        }

        public async Task MountSignUpAsync(string elementId, string afterSignUpUrl, string afterSignInUrl)
        {
            await _js.InvokeVoidAsync("clerkInterop.mountSignUp", elementId, afterSignUpUrl, afterSignInUrl);
        }

        public async Task MountSignInAsync(string elementId, string afterSignInUrl)
        {
            await _js.InvokeVoidAsync("clerkInterop.mountSignIn", elementId, afterSignInUrl);
        }

        public async Task<string?> GetSessionTokenAsync()
        {
            return await _js.InvokeAsync<string?>("clerkInterop.getSessionToken");
        }

        public async Task<ClerkUserInfo> GetCurrentUserAsync()
        {
            return await _js.InvokeAsync<ClerkUserInfo>("clerkInterop.getCurrentUser");
        }

        public async Task<string> GetCurrentUserIdAsync()
        {
            return await _js.InvokeAsync<string>("clerkInterop.getUserId");
        }

    }

}
