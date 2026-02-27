namespace Spydomo.Infrastructure.Clerk
{
    public class ClerkOptions
    {
        public string AppUrl { get; set; } = string.Empty;
        public string FrontendApiBase { get; set; } = string.Empty;
        public string FrontendApiUrl { get; set; } = string.Empty;
        public string RedirectBaseUrl { get; set; } = string.Empty;
        public string SignInRedirectUrl { get; set; } = string.Empty;
        public string SignUpRedirectUrl { get; set; } = string.Empty;
        public string SignupUrl { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string Authority { get; set; } = string.Empty;
        public string PublishableKey { get; set; } = string.Empty;
    }


}
