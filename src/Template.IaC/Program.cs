namespace Template.IaC;

public static class Program
{
    public static void Main()
    {
        var app = new App();

        var envName = app.Node.TryGetContext("env") as string ?? "dev";
        var config = EnvironmentConfig.Load(app, envName);

        _ = new Infrastructure(app, $"template-aws-line-miniapp-{envName}", config, new StackProps
        {
            Env = new Amazon.CDK.Environment
            {
                Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                Region = EnvironmentConfig.Region,
            },
            Description = "LINE mini app template (CloudFront + S3 WASM hosting, HTTP API + Lambda, DynamoDB per-user JSON, LINE auth with a self-issued JWT)",
        });

        app.Synth();
    }
}
