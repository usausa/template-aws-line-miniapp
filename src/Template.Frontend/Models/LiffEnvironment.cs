namespace Template.Frontend.Models;

using System.Text.Json.Serialization;

// Environment facts from the LIFF SDK, shown on the Home page for diagnostics.
public sealed record LiffEnvironment(
    [property: JsonPropertyName("os")] string Os,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("liffVersion")] string LiffVersion,
    [property: JsonPropertyName("lineVersion")] string? LineVersion,
    [property: JsonPropertyName("isInClient")] bool IsInClient);
