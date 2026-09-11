namespace Template.Backend.Services;

using System.Net.Http;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Validates a LINE ID token - the single point where LINE is trusted as the origin of identity.
// Everything downstream relies on this: get it wrong and the whole chain collapses (see SPEC 4.3).
//
// The checklist that must not be skipped:
//   alg = ES256 (fixed)   - blocks 'none' and alg-confusion attacks
//   iss = access.line.me  - blocks tokens from another IdP
//   aud = own channel id  - blocks a valid LINE token issued for a different mini app
//   exp                   - blocks replay of an expired token
//
// The signing keys come from LINE's JWKS endpoint, cached for 15 minutes so that after the first
// fetch there is no outbound call on the hot path. The endpoint is configurable so dev can point
// at a test JWKS and exercise this path without a real LINE handshake (see SPEC 6.2); it never
// changes what is verified.
public sealed class LineTokenValidator : IDisposable
{
    private const string LineIssuer = "https://access.line.me";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly BackendOptions options;
    private readonly HttpClient http;
    private readonly JsonWebTokenHandler handler = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Uri jwksUri;

    private SecurityKey[] cachedKeys = [];
    private DateTimeOffset cacheExpiry = DateTimeOffset.MinValue;

    public LineTokenValidator(BackendOptions options, HttpClient http)
    {
        this.options = options;
        this.http = http;
        jwksUri = new Uri(options.LineJwksUrl);
    }

    // Returns the verified LINE user id (the 'sub' claim), or null if validation fails for any
    // reason. Callers must not surface the reason to clients (a single 401 for every case).
    public async Task<string?> ValidateAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var keys = await GetSigningKeysAsync();

        var result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = LineIssuer,
            ValidAudience = options.LineChannelId,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKeys = keys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        });

        if (!result.IsValid)
        {
            return null;
        }

        return result.ClaimsIdentity.FindFirst("sub")?.Value;
    }

    public void Dispose() => gate.Dispose();

    private async Task<SecurityKey[]> GetSigningKeysAsync()
    {
        if (DateTimeOffset.UtcNow < cacheExpiry && cachedKeys.Length > 0)
        {
            return cachedKeys;
        }

        await gate.WaitAsync();
        try
        {
            if (DateTimeOffset.UtcNow < cacheExpiry && cachedKeys.Length > 0)
            {
                return cachedKeys;
            }

            var json = await http.GetStringAsync(jwksUri);
            var keySet = new JsonWebKeySet(json);
            var keys = keySet.GetSigningKeys().ToArray();

            // Never cache an empty result. A transient fetch problem - or the SPA fallback returning
            // index.html for a not-yet-propagated test JWKS - would otherwise poison the cache for
            // the full TTL and reject every login until it expired. Returning without caching lets
            // the next request retry.
            if (keys.Length == 0)
            {
                return keys;
            }

            cachedKeys = keys;
            cacheExpiry = DateTimeOffset.UtcNow.Add(CacheTtl);
            return cachedKeys;
        }
        finally
        {
            gate.Release();
        }
    }
}
