namespace Template.Frontend.Settings;

// The App section of wwwroot/appsettings.json.
// Every value here is public by design: the LIFF id is a client identifier, and the API endpoint
// is protected by the app JWT, not by keeping the URL secret (SPEC 1.1).
public sealed class AppSetting
{
    // LIFF app id. Selects the LINE channel the SDK initializes against.
    public string LiffId { get; set; } = string.Empty;

    // Base URL of the API. Points at the CloudFront distribution's /api prefix (same-origin in
    // production), not the regional API Gateway endpoint.
    public string ApiEndpoint { get; set; } = string.Empty;
}
