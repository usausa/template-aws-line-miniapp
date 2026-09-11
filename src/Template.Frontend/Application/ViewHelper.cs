namespace Template.Frontend.Application;

// Display helpers used from markup via '@using static'. Kept tiny; formatting is invariant-culture
// because the app ships with InvariantGlobalization.
public static class ViewHelper
{
    // Renders a UTC ISO timestamp for display, or a dash when absent.
    public static string LocalTime(string? iso) =>
        DateTimeOffset.TryParse(
            iso,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value)
            ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : "-";

    // Byte count of a UTF-8 string, for the client-side size hint on the data editor.
    public static int Utf8Bytes(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : System.Text.Encoding.UTF8.GetByteCount(text);
}
