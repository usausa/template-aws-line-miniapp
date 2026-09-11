namespace Template.Backend.Services;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Issues the app JWT - step 3 of the trust chain (SPEC 4.1). Signed with the app's private key,
// which lives only in Secrets Manager, so a client can never forge or alter one.
//
// The token carries the internal user id as 'sub' and the LINE user id as 'line_sub'. line_sub is
// there solely so account deletion can remove the user-auth row (keyed by LINE#{lineUserId})
// without a reverse lookup or a GSI; it must never be logged (SPEC 8.5).
public sealed class JwtIssuer
{
    private readonly BackendOptions options;
    private readonly SigningKeyProvider keyProvider;
    private readonly TimeProvider clock;
    private readonly JsonWebTokenHandler handler = new();

    public JwtIssuer(BackendOptions options, SigningKeyProvider keyProvider, TimeProvider clock)
    {
        this.options = options;
        this.keyProvider = keyProvider;
        this.clock = clock;
    }

    // Returns the signed JWT and its lifetime in seconds (the client uses the latter for display /
    // proactive refresh scheduling; it is not a security boundary - the server checks exp).
    public async Task<(string Token, int ExpiresInSeconds)> IssueAsync(string internalUserId, string lineUserId)
    {
        var key = await keyProvider.GetKeyAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromDays(options.JwtLifetimeDays);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.JwtIssuer,
            Audience = options.JwtAudience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = internalUserId,
                ["line_sub"] = lineUserId,
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
        };

        var token = handler.CreateToken(descriptor);
        return (token, (int)lifetime.TotalSeconds);
    }
}
