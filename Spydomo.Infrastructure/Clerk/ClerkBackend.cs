using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Spydomo.Infrastructure.Clerk
{
    public class ClerkBackend
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly string _apiKey;
        private readonly string _redirectSignupUrl;

        public ClerkBackend(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config["Clerk:SecretKey"]);
            _apiKey = _config["Clerk:SecretKey"];
            _redirectSignupUrl = _config["Clerk:RedirectBaseUrl"] + _config["Clerk:SignupUrl"];
        }

        public async Task UpdateUserPasswordAsync(string clerkUserId, string newPassword)
        {
            var body = new
            {
                password = newPassword
            };

            var response = await _http.PatchAsJsonAsync(
                $"https://api.clerk.dev/v1/users/{clerkUserId}", body);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to update password: {error}");
            }
        }

        public async Task<string> InviteUserAsync(
            string inviteeEmail,
            string inviteeFullName,
            string senderName,
            string? senderEmail = null)
        {
            var split = inviteeFullName?.Trim()?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var firstName = split?.Length > 0 ? split[0] : "";
            var lastName = split?.Length > 1 ? split[1] : "";

            var payload = new
            {
                email_address = inviteeEmail,
                first_name = firstName,
                last_name = lastName,
                redirect_url = _redirectSignupUrl,

                public_metadata = new
                {
                    senderName = senderName,
                    senderEmail = senderEmail
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.clerk.com/v1/invitations")
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Clerk invitation failed: {error}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("id", out var clerkUserIdProp))
                throw new Exception("Clerk invitation response did not contain id");

            return clerkUserIdProp.GetString()!;
        }

        public async Task DeleteUserAsync(string clerkUserId)
        {
            if (clerkUserId.StartsWith("inv_"))
            {
                // Revoke pending invitation
                var revokeRequest = new HttpRequestMessage(HttpMethod.Post, $"https://api.clerk.com/v1/invitations/{clerkUserId}/revoke");
                revokeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                var revokeResponse = await _http.SendAsync(revokeRequest);
                if (!revokeResponse.IsSuccessStatusCode)
                {
                    var content = await revokeResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Clerk invitation revoke failed: {revokeResponse.StatusCode}, {content}");
                }

                return;
            }

            // Delete active Clerk user
            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"https://api.clerk.com/v1/users/{clerkUserId}");
            deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var deleteResponse = await _http.SendAsync(deleteRequest);
            if (!deleteResponse.IsSuccessStatusCode)
            {
                var content = await deleteResponse.Content.ReadAsStringAsync();
                throw new Exception($"Clerk user deletion failed: {deleteResponse.StatusCode}, {content}");
            }

            return;

            throw new Exception("Couldn't find the user or its invitation");
        }

    }

}
