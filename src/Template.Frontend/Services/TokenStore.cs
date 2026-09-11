namespace Template.Frontend.Services;

// Holds the app JWT in memory only. Never written to localStorage or sessionStorage: those are
// reachable by any injected script, and the token is a 7-day bearer credential (SPEC 8.3). If the
// page reloads the store is empty and ApiClient transparently re-exchanges the LINE ID token for a
// new one, which is by design, not an error.
public sealed class TokenStore
{
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

    public string? Token { get; private set; }

    // Valid a little before the real expiry, so a call does not go out with a token about to lapse.
    public bool HasValidToken =>
        Token is not null && DateTimeOffset.UtcNow < expiresAt - TimeSpan.FromSeconds(30);

    public void Set(string value, int expiresInSeconds)
    {
        Token = value;
        expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
    }

    public void Clear()
    {
        Token = null;
        expiresAt = DateTimeOffset.MinValue;
    }
}
