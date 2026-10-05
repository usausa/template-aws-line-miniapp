namespace Template.Frontend.Models;

using System.Text.Json.Serialization;

// JSON contract with the backend. Property names match the API responses in SPEC 6.

public sealed record AuthLoginRequest(
    [property: JsonPropertyName("idToken")] string IdToken);

public sealed record AuthLoginResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);

public sealed record DataGetResponse(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt);

public sealed record DataPutRequest(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("version")] int Version);

public sealed record DataPutResponse(
    [property: JsonPropertyName("version")] int Version);

// Subset of liff.getProfile() surfaced to the UI. Populated by the JS interop layer.
public sealed record LineProfile(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("pictureUrl")] string? PictureUrl,
    [property: JsonPropertyName("statusMessage")] string? StatusMessage);
