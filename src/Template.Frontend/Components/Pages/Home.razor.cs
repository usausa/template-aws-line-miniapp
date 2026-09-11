namespace Template.Frontend.Components.Pages;

// Home: initializes LIFF, shows the LINE profile and environment, and exchanges the LINE ID token
// for the app JWT (the "first mile" of SPEC 2.3). Deliberately shows no raw tokens - only whether a
// session token was acquired (SPEC 10.3).
public sealed partial class Home
{
    [Inject]
    private LiffService Liff { get; set; } = default!;

    [Inject]
    private ApiClient Api { get; set; } = default!;

    [Inject]
    private AppSetting Setting { get; set; } = default!;

    private bool Loading { get; set; } = true;

    private string? Error { get; set; }

    private bool TokenReady { get; set; }

    private LineProfile? Profile { get; set; }

    private LiffEnvironment? Environment { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var ready = await Liff.EnsureInitializedAsync(Setting.LiffId);
            if (!ready)
            {
                // A login redirect is in progress; keep the splash until the page navigates.
                return;
            }

            Profile = await Liff.GetProfileAsync();
            Environment = await Liff.GetEnvironmentAsync();
            TokenReady = await Api.EnsureTokenAsync();
            Loading = false;
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
}
