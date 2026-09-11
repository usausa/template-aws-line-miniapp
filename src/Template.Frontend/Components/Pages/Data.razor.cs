namespace Template.Frontend.Components.Pages;

// Data: the per-user JSON document. Reads on load, edits in a textarea, writes with the version the
// client last read so the server can reject a stale write (409) rather than silently overwriting a
// concurrent edit (SPEC 7.4). Size is hinted client-side, but the server's 413 is the real limit.
public sealed partial class Data
{
    // Matches the server's hard limit (SPEC 7.4), for the client-side hint only.
    private const int MaxBytes = 300 * 1024;

    [Inject]
    private LiffService Liff { get; set; } = default!;

    [Inject]
    private ApiClient Api { get; set; } = default!;

    [Inject]
    private AppSetting Setting { get; set; } = default!;

    private bool Loading { get; set; } = true;

    private bool Saving { get; set; }

    private string? Error { get; set; }

    private string? Status { get; set; }

    private string DataText { get; set; } = string.Empty;

    private int Version { get; set; }

    private string UpdatedAt { get; set; } = string.Empty;

    private bool Oversize => System.Text.Encoding.UTF8.GetByteCount(DataText) > MaxBytes;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var ready = await Liff.EnsureInitializedAsync(Setting.LiffId);
            if (!ready)
            {
                return;
            }

            await LoadAsync();
        }
        catch (JSException ex)
        {
            Error = $"Failed to start: {ex.Message}";
            Loading = false;
        }
        catch (HttpRequestException ex)
        {
            Error = $"Failed to start: {ex.Message}";
            Loading = false;
        }
    }

    private async Task ReloadAsync()
    {
        Status = null;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var result = await Api.GetDataAsync();
        switch (result.Outcome)
        {
            case ApiOutcome.Ok when result.Data is not null:
                DataText = result.Data.Data;
                Version = result.Data.Version;
                UpdatedAt = result.Data.UpdatedAt;
                break;
            case ApiOutcome.NoContent:
                DataText = string.Empty;
                Version = 0;
                UpdatedAt = string.Empty;
                Status = "No data yet. Enter JSON and save.";
                break;
            case ApiOutcome.Unauthorized:
                Error = "Session expired. Please reopen the app.";
                break;
            default:
                Error = "Failed to load data.";
                break;
        }

        Loading = false;
    }

    private async Task SaveAsync()
    {
        Saving = true;
        Status = null;

        try
        {
            var result = await Api.PutDataAsync(DataText, Version);
            switch (result.Outcome)
            {
                case ApiOutcome.Ok:
                    Version = result.Version;
                    await LoadAsync();
                    Status = $"Saved (version {Version}).";
                    break;
                case ApiOutcome.Conflict:
                    Status = "This was updated on another device. Reload before saving again.";
                    break;
                case ApiOutcome.TooLarge:
                    Status = "The document is too large (server limit is 300KB).";
                    break;
                case ApiOutcome.Unauthorized:
                    Error = "Session expired. Please reopen the app.";
                    break;
                default:
                    Status = "Save failed. Please try again.";
                    break;
            }
        }
        catch (JSException)
        {
            Status = "Save failed. Please try again.";
        }
        catch (HttpRequestException)
        {
            Status = "Save failed. Please try again.";
        }
        finally
        {
            Saving = false;
        }
    }
}
