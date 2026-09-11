namespace Template.Backend.Services;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Verifies the app JWT on every protected request - step 4 of the trust chain (SPEC 4.1). This is
// where the internal user id becomes a value the handler can trust and use as the DynamoDB key.
//
// The algorithm is pinned to ES256 so a token re-signed as 'none' or HMAC-over-the-public-key is
// rejected (alg-confusion). Verification uses only the public part of the signing key. On any
// failure the caller returns a single 401 with no reason (SPEC 3.4).
public sealed class OwnTokenValidator
{
    private readonly BackendOptions options;
    private readonly SigningKeyProvider keyProvider;
    private readonly JsonWebTokenHandler handler = new();

    public OwnTokenValidator(BackendOptions options, SigningKeyProvider keyProvider)
    {
        this.options = options;
        this.keyProvider = keyProvider;
    }

    // Entry point for handlers: takes the raw Authorization header (bound by [FromHeader]) and
    // returns the identity, or null for anything short of a fully valid token - missing header,
    // wrong scheme, bad signature, expired. Callers answer every null with the same bare 401.
    public Task<ValidatedUser?> ValidateHeaderAsync(string? authorizationHeader)
    {
        const string scheme = "Bearer ";

        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ValidatedUser?>(null);
        }

        return ValidateAsync(authorizationHeader[scheme.Length..].Trim());
    }

    public async Task<ValidatedUser?> ValidateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var key = await keyProvider.GetKeyAsync();

        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.JwtIssuer,
            ValidAudience = options.JwtAudience,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        });

        if (!result.IsValid)
        {
            return null;
        }

        var sub = result.ClaimsIdentity.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            return null;
        }

        var lineSub = result.ClaimsIdentity.FindFirst("line_sub")?.Value ?? string.Empty;
        return new ValidatedUser(sub, lineSub);
    }
}

// The identity established by a verified app JWT. Sub is the internal user id (the only value used
// to build DynamoDB keys); LineSub is used only by account deletion.
public sealed record ValidatedUser(string Sub, string LineSub);
