namespace Template.Backend.Filters;

using AmazonLambdaExtension.APIGateway;
using AmazonLambdaExtension.Filters;

// Rejects requests that did not come through CloudFront, by checking the x-origin-verify header the
// distribution attaches to /api/* (SPEC 6.1). Applied class-wide via [Filter<OriginVerifyFilter>],
// so every handler - including auth/line - sits behind it.
//
// This is defence in depth, never the authentication: it only proves the request took the intended
// path. Even with this value leaked, the JWT verification in the handlers still stands. An empty
// configured value disables the check (local dev-server without CloudFront in front).
public sealed class OriginVerifyFilter : ILambdaFilter
{
    private readonly BackendOptions options;

    public OriginVerifyFilter(BackendOptions options)
    {
        this.options = options;
    }

    public ValueTask InvokeAsync(LambdaInvocationContext context, LambdaFilterDelegate next)
    {
        if (options.OriginVerify.Length > 0)
        {
            var request = context.GetRequest<APIGatewayHttpApiV2ProxyRequest>();
            if (!string.Equals(FindHeader(request), options.OriginVerify, StringComparison.Ordinal))
            {
                context.Result = HttpResults.Forbid();
                return default;
            }
        }

        return next(context);
    }

    // HTTP API v2 lowercases header names, but read case-insensitively so a hand-crafted test event
    // (e.g. the local Lambda test tool) still resolves.
    private static string? FindHeader(APIGatewayHttpApiV2ProxyRequest request)
    {
        if (request.Headers is null)
        {
            return null;
        }

        foreach (var pair in request.Headers)
        {
            if (string.Equals(pair.Key, "x-origin-verify", StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
