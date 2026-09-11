namespace Template.Backend.Services;

using System.Security.Cryptography;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

using Microsoft.IdentityModel.Tokens;

// Loads the app's ES256 signing key from Secrets Manager and caches it, shared by JwtIssuer (which
// signs) and OwnTokenValidator (which verifies). The secret holds the private key as PKCS#8 PEM;
// the same key object carries the public part used for verification, so no separate public key is
// stored.
//
// The key material never touches an environment variable (SPEC 8.3): only the secret's ARN is
// passed in, and the value is fetched here. Cached 15 minutes to keep GetSecretValue off the hot
// path after the first call in an execution environment.
public sealed class SigningKeyProvider : IDisposable
{
    public const string KeyId = "primary";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly BackendOptions options;
    private readonly IAmazonSecretsManager secrets;
    private readonly SemaphoreSlim gate = new(1, 1);

    // The ECDsa backing the cached key. Held so it can be disposed when the cache refreshes and on
    // shutdown; the DI container disposes this singleton.
    private ECDsa? ecdsa;
    private ECDsaSecurityKey? cachedKey;
    private DateTimeOffset cacheExpiry = DateTimeOffset.MinValue;

    public SigningKeyProvider(BackendOptions options, IAmazonSecretsManager secrets)
    {
        this.options = options;
        this.secrets = secrets;
    }

    public async Task<ECDsaSecurityKey> GetKeyAsync()
    {
        if (cachedKey is not null && DateTimeOffset.UtcNow < cacheExpiry)
        {
            return cachedKey;
        }

        await gate.WaitAsync();
        try
        {
            if (cachedKey is not null && DateTimeOffset.UtcNow < cacheExpiry)
            {
                return cachedKey;
            }

            var response = await secrets.GetSecretValueAsync(
                new GetSecretValueRequest { SecretId = options.JwtSecretArn });

            var created = ECDsa.Create();
            created.ImportFromPem(response.SecretString);

            ecdsa?.Dispose();
            ecdsa = created;
            cachedKey = new ECDsaSecurityKey(created) { KeyId = KeyId };
            cacheExpiry = DateTimeOffset.UtcNow.Add(CacheTtl);
            return cachedKey;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        ecdsa?.Dispose();
        gate.Dispose();
    }
}
