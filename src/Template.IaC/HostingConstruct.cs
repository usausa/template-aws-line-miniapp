namespace Template.IaC;

using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.S3;

// Application hosting: private S3 bucket + CloudFront (OAC), same shape as the S3+Cognito template.
// Two additions for this app:
//   - the /api/* behavior attaches an x-origin-verify custom header, so the backend can reject
//     requests that reach execute-api without going through this distribution (SPEC 6.1);
//   - the CSP is narrowed to the LINE hosts instead of Cognito/S3.
public sealed class HostingConstruct : Construct
{
    public HostingConstruct(
        Construct scope, string id, EnvironmentConfig config, string apiOriginHost)
        : base(scope, id)
    {
        Bucket = new Bucket(this, "Bucket", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            RemovalPolicy = config.Ephemeral ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN,
            AutoDeleteObjects = config.Ephemeral,
        });

        var headersPolicy = new ResponseHeadersPolicy(this, "Headers", new ResponseHeadersPolicyProps
        {
            SecurityHeadersBehavior = new ResponseSecurityHeadersBehavior
            {
                ContentTypeOptions = new ResponseHeadersContentTypeOptions { Override = true },
                FrameOptions = new ResponseHeadersFrameOptions
                {
                    FrameOption = HeadersFrameOption.DENY,
                    Override = true,
                },
                ReferrerPolicy = new ResponseHeadersReferrerPolicy
                {
                    ReferrerPolicy = HeadersReferrerPolicy.STRICT_ORIGIN_WHEN_CROSS_ORIGIN,
                    Override = true,
                },
                StrictTransportSecurity = new ResponseHeadersStrictTransportSecurity
                {
                    AccessControlMaxAge = Duration.Days(365),
                    IncludeSubdomains = true,
                    Override = true,
                },
                ContentSecurityPolicy = new ResponseHeadersContentSecurityPolicy
                {
                    ContentSecurityPolicy = Csp,
                    Override = true,
                },
            },
        });

        Distribution = new Distribution(this, "Distribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(Bucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                Compress = true,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                ResponseHeadersPolicy = headersPolicy,
            },
            DefaultRootObject = "index.html",
            HttpVersion = HttpVersion.HTTP2_AND_3,

            // Include Japan (PRICE_CLASS_100 covers only NA/EU and would route to distant edges).
            PriceClass = PriceClass.PRICE_CLASS_200,

            // SPA fallback. Missing keys come back as 403 through OAC, so map both 403/404 to
            // index.html.
            ErrorResponses =
            [
                new ErrorResponse
                {
                    HttpStatus = 403,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html",
                    Ttl = Duration.Seconds(10),
                },
                new ErrorResponse
                {
                    HttpStatus = 404,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html",
                    Ttl = Duration.Seconds(10),
                },
            ],
            Comment = $"LINE mini app ({config.EnvName})",
        });

        // Serve the API from the app's own origin: same-origin removes the CORS preflight from every
        // production call and lets the CSP cover the API with 'self'. Nothing is cached and the
        // viewer's headers are forwarded (the Authorization header carries the app JWT); the Host
        // header is not forwarded so API Gateway still routes by its own domain. The x-origin-verify
        // header is injected here and checked by the backend, so a request straight to execute-api
        // (bypassing this distribution) is rejected.
        Distribution.AddBehavior(
            $"{ApiConstruct.PathPrefix}/*",
            new HttpOrigin(apiOriginHost, new HttpOriginProps
            {
                CustomHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x-origin-verify"] = config.OriginVerify,
                },
            }),
            new AddBehaviorOptions
            {
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                AllowedMethods = AllowedMethods.ALLOW_ALL,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
            });
    }

    public Bucket Bucket { get; }

    public Distribution Distribution { get; }

    // Minimal CSP that lets Blazor WASM run and reaches only the LINE platform.
    //   connect-src  api.line.me     - LIFF SDK's XHR (init, getProfile, ...). The API is 'self'.
    //   script-src   static.line-scdn.net - the LIFF SDK bundle. No inline or other external script.
    //   img-src      profile.line-scdn.net - LINE profile pictures.
    // The LIFF SDK's exact set of hosts is not published; if a violation shows up on a real device,
    // add the host here (SPEC 10.1).
    private const string Csp =
        "default-src 'self'; " +
        "connect-src 'self' https://api.line.me; " +
        "script-src 'self' 'wasm-unsafe-eval' https://static.line-scdn.net; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https://profile.line-scdn.net; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "object-src 'none'";
}
