namespace Template.Frontend.Services;

// Typed wrapper over window.liffInterop (wwwroot/js/liff-interop.js).
//
// Initialization is memoised: the first caller runs liff.init, everyone else awaits the same task.
// liff.init must not run twice, and pages initialize independently, so this keeps a single init
// regardless of which page loads first.
public sealed class LiffService
{
    private readonly IJSRuntime js;
    private Task<bool>? initTask;

    public LiffService(IJSRuntime js)
    {
        this.js = js;
    }

    // Returns true when LIFF is ready and logged in; false while a login redirect is in progress
    // (the caller should render nothing further and let the navigation happen).
    public Task<bool> EnsureInitializedAsync(string liffId) =>
        initTask ??= js.InvokeAsync<bool>("liffInterop.init", liffId).AsTask();

    public async Task<string?> GetFreshIdTokenAsync() =>
        await js.InvokeAsync<string?>("liffInterop.getFreshIdToken");

    public async Task<LineProfile> GetProfileAsync() =>
        await js.InvokeAsync<LineProfile>("liffInterop.getProfile");

    public async Task<LiffEnvironment> GetEnvironmentAsync() =>
        await js.InvokeAsync<LiffEnvironment>("liffInterop.getEnvironment");

    public async Task LogoutAsync() =>
        await js.InvokeVoidAsync("liffInterop.logout");

    public async Task CloseWindowAsync() =>
        await js.InvokeVoidAsync("liffInterop.closeWindow");
}
