namespace Template.IaC;

// Single-stack layout (the class name avoids the 'Stack' suffix to satisfy CA1711).
//
// Creation order is forced by a one-way chain of references:
//   API (bare) - depends on nothing here
//   Hosting    - needs the API host for its /api/* origin
//   Data       - the DynamoDB tables (no dependency)
//   Secret     - the JWT signing key holder (no dependency)
//   API routes - need the table names, the secret ARN, and (dev) the distribution domain for the
//                test-JWKS URL
// Creating the API bare first and attaching routes afterwards is what keeps this acyclic.
public sealed class Infrastructure : Stack
{
    public Infrastructure(Construct scope, string id, EnvironmentConfig config, IStackProps props)
        : base(scope, id, props)
    {
        var api = new ApiConstruct(this, "Api", config);

        var hosting = new HostingConstruct(this, "Hosting", config, api.OriginHost);

        var appOrigin = $"https://{hosting.Distribution.DistributionDomainName}";

        var data = new DataConstruct(this, "Data", config);
        var secret = new SecretConstruct(this, "Secret", config);

        // dev with testJwks points the LINE validator at a JWKS served from the app bucket, so the
        // whole auth path can be tested without a real LINE handshake (SPEC 6.2). Never set for prod.
        var jwksUrl = config.TestJwks ? $"{appOrigin}/test-jwks.json" : string.Empty;

        api.AddRoutes(data, secret, jwksUrl);

        //--------------------------------------------------------------------------------
        // Outputs (consumed by scripts/*.ps1)
        //--------------------------------------------------------------------------------

        _ = new CfnOutput(this, "CloudFrontDomain", new CfnOutputProps { Value = hosting.Distribution.DistributionDomainName });
        _ = new CfnOutput(this, "DistributionId", new CfnOutputProps { Value = hosting.Distribution.DistributionId });
        _ = new CfnOutput(this, "AppBucketName", new CfnOutputProps { Value = hosting.Bucket.BucketName });

        // The app talks to the API through CloudFront, so this is the app origin plus the prefix.
        _ = new CfnOutput(this, "ApiEndpoint", new CfnOutputProps { Value = $"{appOrigin}{ApiConstruct.PathPrefix}" });

        // The regional execute-api endpoint, used by the acceptance test to prove a direct call
        // (bypassing CloudFront, so without x-origin-verify) is rejected with 403.
        _ = new CfnOutput(this, "DirectApiEndpoint", new CfnOutputProps { Value = $"https://{api.OriginHost}" });

        _ = new CfnOutput(this, "AuthTableName", new CfnOutputProps { Value = data.AuthTable.TableName });
        _ = new CfnOutput(this, "DataTableName", new CfnOutputProps { Value = data.DataTable.TableName });
        _ = new CfnOutput(this, "JwtSecretArn", new CfnOutputProps { Value = secret.Secret.SecretArn });
        _ = new CfnOutput(this, "LineChannelId", new CfnOutputProps { Value = config.LineChannelId });
        _ = new CfnOutput(this, "LiffId", new CfnOutputProps { Value = config.LiffId });
        _ = new CfnOutput(this, "LiffIdLocal", new CfnOutputProps { Value = config.LiffIdLocal });
    }
}
