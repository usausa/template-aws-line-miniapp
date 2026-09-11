namespace Template.Backend;

using System.Net.Http;

using Amazon.DynamoDBv2;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.SecretsManager;

using AmazonLambdaExtension.Serialization;
using AmazonLambdaExtension.Validation;

using Microsoft.Extensions.DependencyInjection;

using Template.Backend.Application;
using Template.Backend.Filters;
using Template.Backend.Services;

// DI container for the generated handler wrappers ([ServiceResolver] on MiniAppFunction). Built
// once per Lambda execution environment, so the singletons here (AWS clients, cached keys, the
// JWKS cache) survive across warm invocations and their setup cost is paid once.
public static class ServiceResolver
{
    public static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        // Serialization used by the generated code: ILambdaSerializer turns HttpResult bodies into
        // the proxy response, IBodySerializer binds [FromBody] parameters, and the validator runs
        // DataAnnotations over bound bodies. All source-generated JSON - no reflection.
        services.AddSingleton<ILambdaSerializer>(new SourceGeneratorLambdaJsonSerializer<FunctionSerializerContext>());
        services.AddSingleton<IBodySerializer>(new JsonBodySerializer(FunctionSerializerContext.Default));
        services.AddSingleton<IRequestValidator, DataAnnotationsRequestValidator>();

        services.AddSingleton(BackendOptions.FromEnvironment());
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient());
        services.AddSingleton(_ => new HttpClient());

        services.AddSingleton<LineTokenValidator>();
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<JwtIssuer>();
        services.AddSingleton<OwnTokenValidator>();
        services.AddSingleton<UserRepository>();

        services.AddSingleton<OriginVerifyFilter>();

        return services;
    }
}
