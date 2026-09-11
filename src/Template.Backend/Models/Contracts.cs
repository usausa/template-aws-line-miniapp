namespace Template.Backend.Models;

using System.ComponentModel.DataAnnotations;

// Request/response shapes for the API. Property names are the JSON contract with the Blazor client.
// [FromBody] parameters are validated with these DataAnnotations by the generated wrapper; a
// violation returns 400 before the handler runs.

// POST /api/auth/line - exchanges a LINE ID token for an app JWT. IdToken is deliberately not
// [Required]: its checks belong to token validation, which answers with an undifferentiated 401.
public sealed record LoginRequest(
    [property: JsonPropertyName("idToken")] string? IdToken);

public sealed record LoginResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);

// GET /api/data - the caller's stored JSON, or 204 when nothing is stored yet.
public sealed record DataResponse(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt);

// PUT /api/data - optimistic-locked write. Version is the version the client last read.
public sealed record PutRequest(
    [property: JsonPropertyName("data")][property: Required(AllowEmptyStrings = true)] string Data,
    [property: JsonPropertyName("version")][property: Range(0, int.MaxValue)] int Version);

public sealed record PutResponse(
    [property: JsonPropertyName("version")] int Version);

public sealed record ErrorResponse(
    [property: JsonPropertyName("message")] string Message);
