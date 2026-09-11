namespace Template.IaC;

using System.Security.Cryptography;
using System.Text;

// Reads per-environment settings from the cdk.json context. Switch with -c env=dev|prod.
public sealed class EnvironmentConfig
{
    // Target region.
    public const string Region = "ap-northeast-1";

    // Local dev server URL. HTTPS because LIFF only allows https endpoint URLs; must match
    // Template.Frontend/Properties/launchSettings.json.
    public const string LocalhostOrigin = "https://localhost:5250";

    private EnvironmentConfig(
        string envName,
        string lineChannelId,
        string liffId,
        string liffIdLocal,
        bool allowLocalhost,
        bool testJwks)
    {
        EnvName = envName;
        LineChannelId = lineChannelId;
        LiffId = liffId;
        LiffIdLocal = liffIdLocal;
        AllowLocalhost = allowLocalhost;
        TestJwks = testJwks;
    }

    public string EnvName { get; }

    // LINE channel id, verified as the ID token audience (SPEC 4.3).
    public string LineChannelId { get; }

    // LIFF app id for the deployed site, and for the local dev server. The deployed and local sites
    // have different endpoint URLs, so LINE requires a separate LIFF app for each.
    public string LiffId { get; }

    public string LiffIdLocal { get; }

    // True only for dev: adds localhost to the API CORS allowance.
    public bool AllowLocalhost { get; }

    // dev only: point the LINE token validator at a test JWKS so the whole auth path can be
    // exercised without a real LINE handshake (SPEC 6.2). Rejected for prod so it cannot ship.
    public bool TestJwks { get; }

    // dev tears down cleanly on stack deletion; prod retains data.
    public bool Ephemeral => !String.Equals(EnvName, "prod", StringComparison.Ordinal);

    // The value CloudFront attaches as x-origin-verify and Lambda checks (SPEC 6.1). Derived from
    // the account and environment so it is stable across deploys yet not committed to source. Not a
    // cryptographic secret - defence in depth only, never the authentication.
    public string OriginVerify
    {
        get
        {
            var account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT") ?? "local";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{account}:{EnvName}:line-miniapp-origin"));
            return Convert.ToHexString(hash)[..32];
        }
    }

    public static EnvironmentConfig Load(App app, string envName)
    {
        if (app.Node.TryGetContext(envName) is not IDictionary<string, object> context)
        {
            throw new InvalidOperationException($"cdk.json has no context for environment '{envName}'.");
        }

        var testJwks = context.TryGetValue("testJwks", out var t) && t is true;
        var ephemeral = !String.Equals(envName, "prod", StringComparison.Ordinal);
        if (testJwks && !ephemeral)
        {
            // The test JWKS accepts forged tokens; it must never exist in a non-ephemeral (prod)
            // environment. Fail synth rather than deploy it.
            throw new InvalidOperationException("testJwks must not be enabled for a non-ephemeral environment.");
        }

        return new EnvironmentConfig(
            envName,
            RequireString(context, "lineChannelId", envName),
            RequireString(context, "liffId", envName),
            context.TryGetValue("liffIdLocal", out var local) && local is string s ? s : string.Empty,
            context.TryGetValue("allowLocalhost", out var allow) && allow is true,
            testJwks);
    }

    private static string RequireString(IDictionary<string, object> context, string key, string envName) =>
        context.TryGetValue(key, out var value) && value is string s && s.Length > 0
            ? s
            : throw new InvalidOperationException($"'{key}' is missing for environment '{envName}'.");
}
