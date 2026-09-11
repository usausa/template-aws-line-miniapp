namespace Template.Frontend.Models;

using System.Text.Json.Serialization;

// JSON contract with the backend. Property names match the API responses in SPEC 6.

public sealed record LoginRequest(
    [property: JsonPropertyName("idToken")] string IdToken);

public sealed record LoginResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);

public sealed record DataResponse(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt);

public sealed record PutRequest(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("version")] int Version);

public sealed record PutResponse(
    [property: JsonPropertyName("version")] int Version);

// Subset of liff.getProfile() surfaced to the UI. Populated by the JS interop layer.
public sealed record LineProfile(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("pictureUrl")] string? PictureUrl,
    [property: JsonPropertyName("statusMessage")] string? StatusMessage);
