namespace Template.Backend.Functions;

using System.Net;

using AmazonLambdaExtension.Annotations;
using AmazonLambdaExtension.APIGateway;

using Template.Backend.Filters;

// All four API handlers of the mini app, wrapped by the AmazonLambdaExtension source generator.
// Each [HttpApi] method becomes an independent Lambda entry point
// ({Method}_Handler; wired to its route by the CDK stack), all sharing this one publish artifact -
// same layout as the Annotations version, one class instead of four.
//
// The x-origin-verify check runs class-wide as a filter (SPEC 6.1). Token verification stays inside
// the protected handlers on purpose: the verified 'sub' is the only source of the DynamoDB key, and
// keeping that step visible in the method is the point of the design (SPEC 1.3). The route
// templates here are documentation and route-parameter binding only; actual routing is defined in
// the CDK stack.
[Lambda]
[ServiceResolver(typeof(ServiceResolver))]
[Filter<OriginVerifyFilter>]
public partial class MiniAppFunction
{
    // Warn well before the limit; refuse before the item can approach DynamoDB's 400 KB ceiling.
    private const int WarnBytes = 100 * 1024;
    private const int MaxBytes = 300 * 1024;

    private readonly LineTokenValidator lineValidator;
    private readonly OwnTokenValidator tokenValidator;
    private readonly JwtIssuer issuer;
    private readonly UserRepository users;

    public MiniAppFunction(
        LineTokenValidator lineValidator, OwnTokenValidator tokenValidator, JwtIssuer issuer, UserRepository users)
    {
        this.lineValidator = lineValidator;
        this.tokenValidator = tokenValidator;
        this.issuer = issuer;
        this.users = users;
    }

    // POST /api/auth/line - the one place LINE is the trusted origin of identity (SPEC 4.1).
    // Verifies the LINE ID token, resolves it to an internal user id (creating one on first login),
    // and returns an app JWT. Any validation failure is a bare 401 - the reason is never disclosed
    // (SPEC 3.4).
    [HttpApi(LambdaHttpMethod.Post, "/api/auth/line")]
    public async ValueTask<IHttpResult> Login([FromBody] LoginRequest request, ILambdaContext context)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return HttpResults.Unauthorized();
        }

        var lineUserId = await lineValidator.ValidateAsync(request.IdToken);
        if (lineUserId is null)
        {
            return HttpResults.Unauthorized();
        }

        var internalUserId = await users.ResolveOrCreateInternalUserIdAsync(lineUserId);
        var (token, expiresIn) = await issuer.IssueAsync(internalUserId, lineUserId);

        // userId only - never the LINE user id (SPEC 8.5).
        context.Logger.LogInformation(
            $"{{\"event\":\"auth.line\",\"userId\":\"{internalUserId}\",\"requestId\":\"{context.AwsRequestId}\"}}");

        return HttpResults.Ok(new LoginResponse(token, expiresIn));
    }

    // GET /api/data - returns the caller's stored JSON, or 204 when nothing is stored yet.
    // The user id comes only from the verified token; there is no userId parameter to tamper with
    // (SPEC 7.3).
    [HttpApi(LambdaHttpMethod.Get, "/api/data")]
    public async ValueTask<IHttpResult> GetData(
        ILambdaContext context,
        [FromHeader("authorization")] string authorization = "")
    {
        var user = await tokenValidator.ValidateHeaderAsync(authorization);
        if (user is null)
        {
            return HttpResults.Unauthorized();
        }

        var data = await users.GetDataAsync(user.Sub);
        if (data is null)
        {
            return HttpResults.NoContent();
        }

        context.Logger.LogInformation(
            $"{{\"event\":\"data.read\",\"userId\":\"{user.Sub}\",\"requestId\":\"{context.AwsRequestId}\"}}");

        return HttpResults.Ok(data);
    }

    // PUT /api/data - optimistic-locked write of the caller's JSON.
    //   200 -> stored, returns the new version
    //   409 -> the client's version is stale (another device wrote first); re-read and merge
    //   413 -> the payload exceeds the hard limit, refused before it can approach DynamoDB's 400 KB
    [HttpApi(LambdaHttpMethod.Put, "/api/data")]
    public async ValueTask<IHttpResult> PutData(
        [FromBody] PutRequest request,
        ILambdaContext context,
        [FromHeader("authorization")] string authorization = "")
    {
        var user = await tokenValidator.ValidateHeaderAsync(authorization);
        if (user is null)
        {
            return HttpResults.Unauthorized();
        }

        var bytes = Encoding.UTF8.GetByteCount(request.Data);
        if (bytes > MaxBytes)
        {
            return HttpResults.NewResult(HttpStatusCode.RequestEntityTooLarge);
        }

        if (bytes > WarnBytes)
        {
            context.Logger.LogWarning(
                $"{{\"event\":\"data.size_warn\",\"userId\":\"{user.Sub}\",\"bytes\":{bytes}}}");
        }

        var outcome = await users.PutDataAsync(user.Sub, request.Data, request.Version);
        if (!outcome.Applied)
        {
            return HttpResults.Conflict();
        }

        context.Logger.LogInformation(
            $"{{\"event\":\"data.write\",\"userId\":\"{user.Sub}\",\"version\":{outcome.NewVersion},\"requestId\":\"{context.AwsRequestId}\"}}");

        return HttpResults.Ok(new PutResponse(outcome.NewVersion));
    }

    // DELETE /api/account - removes the user's auth mapping and data in one transaction (SPEC 7.5).
    // Both keys come from the verified token: sub (data row) and line_sub (auth row) - the whole
    // reason line_sub rides in the JWT (SPEC 4.2).
    [HttpApi(LambdaHttpMethod.Delete, "/api/account")]
    public async ValueTask<IHttpResult> DeleteAccount(
        ILambdaContext context,
        [FromHeader("authorization")] string authorization = "")
    {
        var user = await tokenValidator.ValidateHeaderAsync(authorization);
        if (user is null)
        {
            return HttpResults.Unauthorized();
        }

        await users.DeleteAccountAsync(user.Sub, user.LineSub);

        context.Logger.LogInformation(
            $"{{\"event\":\"account.delete\",\"userId\":\"{user.Sub}\",\"requestId\":\"{context.AwsRequestId}\"}}");

        return HttpResults.NoContent();
    }
}
