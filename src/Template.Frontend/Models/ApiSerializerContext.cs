namespace Template.Frontend.Models;

using System.Text.Json.Serialization;

// Source-generated JSON for the API calls, so the trimmed/AOT build keeps working without runtime
// reflection over these types.
[JsonSerializable(typeof(AuthLoginRequest))]
[JsonSerializable(typeof(AuthLoginResponse))]
[JsonSerializable(typeof(DataGetResponse))]
[JsonSerializable(typeof(DataPutRequest))]
[JsonSerializable(typeof(DataPutResponse))]
[JsonSerializable(typeof(LineProfile))]
[JsonSerializable(typeof(LiffEnvironment))]
public sealed partial class ApiSerializerContext : JsonSerializerContext;
