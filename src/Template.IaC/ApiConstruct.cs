namespace Template.IaC;

using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AwsApigatewayv2Integrations;

// Both the API and Lambda namespaces define HttpMethod.
using HttpMethod = Amazon.CDK.AWS.Apigatewayv2.HttpMethod;

// The API: CloudFront /api/* -> HTTP API -> Lambda. Unlike the S3+Cognito template there is no API
// Gateway JWT authorizer - the app issues and verifies its own JWT, so authentication happens
// inside the Lambda (RequestGate + OwnTokenValidator). What the gateway provides is routing and
// throttling; the origin-verify header check (defence in depth) and all real auth are in code.
//
// Construction is two-phase to keep the resource graph acyclic: the bare API is created first (it
// depends on nothing here) so the distribution can point its /api/* origin at it, then AddRoutes
// wires the functions once the tables, secret, and CloudFront domain exist.
public sealed class ApiConstruct : Construct
{
    // Paths are prefixed so CloudFront can forward /api/* through unchanged.
    public const string PathPrefix = "/api";

    // Published output of Template.Backend, produced by scripts/deploy-api.ps1. Every function
    // shares this one artifact and differs only by handler.
    private static readonly string Artifact =
        System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "publish-api");

    private readonly EnvironmentConfig config;

    public ApiConstruct(Construct scope, string id, EnvironmentConfig config)
        : base(scope, id)
    {
        this.config = config;

        // CORS is needed only for the local dev server, which calls the dev distribution
        // cross-origin. In production the browser reaches the API same-origin through CloudFront, so
        // no preflight occurs.
        var cors = config.AllowLocalhost
            ? new CorsPreflightOptions
            {
                AllowOrigins = [EnvironmentConfig.LocalhostOrigin],
                AllowMethods = [CorsHttpMethod.GET, CorsHttpMethod.PUT, CorsHttpMethod.POST, CorsHttpMethod.DELETE],
                AllowHeaders = ["authorization", "content-type"],
            }
            : null;

        Api = new HttpApi(this, "Api", new HttpApiProps
        {
            Description = $"LINE mini app API ({config.EnvName})",
            CorsPreflight = cors,
        });

        // Modest throttling in place of a WAF rate rule (SPEC 1 #3 / 9.4): caps request-driven cost
        // amplification. Not an auth control.
        if (Api.DefaultStage?.Node.DefaultChild is CfnStage stage)
        {
            stage.DefaultRouteSettings = new CfnStage.RouteSettingsProperty
            {
                ThrottlingBurstLimit = 100,
                ThrottlingRateLimit = 50,
            };
        }
    }

    public HttpApi Api { get; }

    // Regional endpoint host, used as the CloudFront origin for /api/*.
    public string OriginHost => $"{Api.ApiId}.execute-api.{EnvironmentConfig.Region}.amazonaws.com";

    // Wires the four functions once their dependencies exist. jwksUrl overrides the LINE JWKS
    // endpoint (dev test-JWKS only); empty means the backend uses the real LINE endpoint.
    public void AddRoutes(DataConstruct data, SecretConstruct secret, string jwksUrl)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AUTH_TABLE"] = data.AuthTable.TableName,
            ["DATA_TABLE"] = data.DataTable.TableName,
            ["LINE_CHANNEL_ID"] = config.LineChannelId,
            ["JWT_SECRET_ARN"] = secret.Secret.SecretArn,
            ["JWT_ISSUER"] = "template-aws-line-miniapp",
            ["JWT_AUDIENCE"] = "miniapp",
            ["JWT_LIFETIME_DAYS"] = "7",
            ["ORIGIN_VERIFY"] = config.OriginVerify,
        };

        if (!string.IsNullOrEmpty(jwksUrl))
        {
            env["LINE_JWKS_URL"] = jwksUrl;
        }

        var authLine = AddRoute(env, "AuthLine", "Login", HttpMethod.POST, "/auth/line");
        data.AuthTable.Grant(authLine, "dynamodb:GetItem", "dynamodb:PutItem");
        secret.Secret.GrantRead(authLine);

        var dataGet = AddRoute(env, "DataGet", "GetData", HttpMethod.GET, "/data");
        data.DataTable.Grant(dataGet, "dynamodb:GetItem");
        secret.Secret.GrantRead(dataGet);

        var dataPut = AddRoute(env, "DataPut", "PutData", HttpMethod.PUT, "/data");
        data.DataTable.Grant(dataPut, "dynamodb:PutItem");
        secret.Secret.GrantRead(dataPut);

        var accountDelete = AddRoute(env, "AccountDelete", "DeleteAccount", HttpMethod.DELETE, "/account");
        data.AuthTable.Grant(accountDelete, "dynamodb:DeleteItem");
        data.DataTable.Grant(accountDelete, "dynamodb:DeleteItem");
        secret.Secret.GrantRead(accountDelete);
    }

    // handlerMethod is a [HttpApi] method of MiniAppFunction; the AmazonLambdaExtension source
    // generator emits its Lambda entry point as {Method}_Handler on the same class.
    private Function AddRoute(
        IDictionary<string, string> env, string name, string handlerMethod, HttpMethod method, string path)
    {
        var function = new Function(this, $"{name}Function", new FunctionProps
        {
            Runtime = Runtime.DOTNET_10,
            Handler = $"Template.Backend::Template.Backend.Functions.MiniAppFunction::{handlerMethod}_Handler",
            Code = Code.FromAsset(Artifact),
            MemorySize = 256,
            Timeout = Duration.Seconds(10),
            Environment = new Dictionary<string, string>(env, StringComparer.Ordinal),
            LogGroup = new LogGroup(this, $"{name}Logs", new LogGroupProps
            {
                Retention = config.Ephemeral ? RetentionDays.ONE_WEEK : RetentionDays.ONE_MONTH,
                RemovalPolicy = config.Ephemeral ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN,
            }),
            Description = $"{name} ({config.EnvName})",
        });

        // Structurally forbid table-wide reads. This API completes every request with point
        // operations; a stray Scan/Query (e.g. after a future edit) could return another user's
        // data, so it is denied at the role level rather than trusted not to happen (SPEC 9.4).
        function.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.DENY,
            Actions = ["dynamodb:Scan", "dynamodb:Query"],
            Resources = ["*"],
        }));

        Api.AddRoutes(new AddRoutesOptions
        {
            Path = $"{PathPrefix}{path}",
            Methods = [method],
            Integration = new HttpLambdaIntegration($"{name}Integration", function),
        });

        return function;
    }
}
