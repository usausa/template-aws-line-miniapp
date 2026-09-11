namespace Template.IaC;

using Amazon.CDK.AWS.SecretsManager;

// Holder for the app's ES256 JWT signing key (SPEC 8.3). The stack creates only the container; the
// PEM value is put in afterwards by scripts/init-jwt-key.ps1, so no private key material is ever in
// the CDK template, the CloudFormation events, or source control.
//
// A placeholder value is set at creation because Secrets Manager requires one; init-jwt-key.ps1
// overwrites it with a real key and refuses to run twice unless forced.
public sealed class SecretConstruct : Construct
{
    public SecretConstruct(Construct scope, string id, EnvironmentConfig config)
        : base(scope, id)
    {
        Secret = new Secret(this, "JwtSigningKey", new SecretProps
        {
            Description = $"ES256 JWT signing key (PEM) for the LINE mini app ({config.EnvName}). Set by scripts/init-jwt-key.ps1.",
            SecretStringValue = SecretValue.UnsafePlainText("PLACEHOLDER-run-init-jwt-key.ps1"),
            RemovalPolicy = config.Ephemeral ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN,
        });
    }

    public Secret Secret { get; }
}
