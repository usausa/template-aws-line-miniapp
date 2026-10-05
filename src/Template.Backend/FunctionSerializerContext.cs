namespace Template.Backend;

// Shared by every function in this assembly: the LambdaSerializer attribute in Assembly.cs is
// assembly-wide. Adding a function means adding its request/response types here.
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
[JsonSerializable(typeof(AuthLoginRequest))]
[JsonSerializable(typeof(AuthLoginResponse))]
[JsonSerializable(typeof(DataGetResponse))]
[JsonSerializable(typeof(DataPutRequest))]
[JsonSerializable(typeof(DataPutResponse))]
[JsonSerializable(typeof(ErrorResponse))]
public sealed partial class FunctionSerializerContext : JsonSerializerContext;
