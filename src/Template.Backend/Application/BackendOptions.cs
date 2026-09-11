namespace Template.Backend.Application;

// Configuration read once from environment variables set by the CDK stack (see ApiConstruct).
// No secret material lives here: JWT_SECRET_ARN is only the address of the signing key, which
// is fetched from Secrets Manager at runtime (JwtIssuer / OwnTokenValidator).
public sealed class BackendOptions
{
    public string AuthTable { get; }

    public string DataTable { get; }

    public string LineChannelId { get; }

    public string LineJwksUrl { get; }

    public string JwtSecretArn { get; }

    public string JwtIssuer { get; }

    public string JwtAudience { get; }

    public int JwtLifetimeDays { get; }

    // Value CloudFront attaches as x-origin-verify. Not a secret in the cryptographic sense; it
    // is a defence-in-depth check that the request came through the distribution, never the
    // authentication itself (that is the JWT). Empty disables the check (local dev-server).
    public string OriginVerify { get; }

    private BackendOptions(
        string authTable,
        string dataTable,
        string lineChannelId,
        string lineJwksUrl,
        string jwtSecretArn,
        string jwtIssuer,
        string jwtAudience,
        int jwtLifetimeDays,
        string originVerify)
    {
        AuthTable = authTable;
        DataTable = dataTable;
        LineChannelId = lineChannelId;
        LineJwksUrl = lineJwksUrl;
        JwtSecretArn = jwtSecretArn;
        JwtIssuer = jwtIssuer;
        JwtAudience = jwtAudience;
        JwtLifetimeDays = jwtLifetimeDays;
        OriginVerify = originVerify;
    }

    public static BackendOptions FromEnvironment()
    {
        var jwksUrl = Optional("LINE_JWKS_URL", "https://api.line.me/oauth2/v2.1/certs");
        var issuer = Optional("JWT_ISSUER", "template-aws-line-miniapp");
        var audience = Optional("JWT_AUDIENCE", "miniapp");
        var lifetimeDays = Int32.TryParse(
            Environment.GetEnvironmentVariable("JWT_LIFETIME_DAYS"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var days)
            ? days
            : 7;

        return new BackendOptions(
            Require("AUTH_TABLE"),
            Require("DATA_TABLE"),
            Require("LINE_CHANNEL_ID"),
            jwksUrl,
            Require("JWT_SECRET_ARN"),
            issuer,
            audience,
            lifetimeDays,
            Environment.GetEnvironmentVariable("ORIGIN_VERIFY") ?? string.Empty);
    }

    private static string Require(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

    private static string Optional(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
