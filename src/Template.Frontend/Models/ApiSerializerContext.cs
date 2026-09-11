namespace Template.Frontend.Models;

using System.Text.Json.Serialization;

// Source-generated JSON for the API calls, so the trimmed/AOT build keeps working without runtime
// reflection over these types.
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(DataResponse))]
[JsonSerializable(typeof(PutRequest))]
[JsonSerializable(typeof(PutResponse))]
[JsonSerializable(typeof(LineProfile))]
[JsonSerializable(typeof(LiffEnvironment))]
public sealed partial class ApiSerializerContext : JsonSerializerContext;
