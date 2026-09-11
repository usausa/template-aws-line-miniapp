namespace Template.Frontend.Components.Pages;

// Account: withdrawal. On confirm, calls DELETE /api/account (which removes both DynamoDB rows in
// one transaction), then clears the in-memory token, logs out of LIFF, and closes the window when
// running inside the LINE client (SPEC 7.5 / 7.4).
public sealed partial class Account
{
    [Inject]
    private LiffService Liff { get; set; } = default!;

    [Inject]
    private ApiClient Api { get; set; } = default!;

    [Inject]
    private TokenStore Tokens { get; set; } = default!;

    [Inject]
    private AppSetting Setting { get; set; } = default!;

    private bool Ready { get; set; }

    private bool Confirming { get; set; }

    private bool Deleting { get; set; }

    private bool Done { get; set; }

    private string? Error { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var ready = await Liff.EnsureInitializedAsync(Setting.LiffId);
            if (!ready)
            {
                return;
            }

            Ready = true;
        }
        catch (JSException ex)
        {
            Error = $"Failed to start: {ex.Message}";
            Ready = true;
        }
    }

    private async Task DeleteAsync()
    {
        Deleting = true;
        try
        {
            var outcome = await Api.DeleteAccountAsync();
            if (outcome is ApiOutcome.Ok)
            {
                Tokens.Clear();
                Done = true;
                await Liff.LogoutAsync();
                await Liff.CloseWindowAsync();
            }
            else
            {
                Error = "Failed to delete the account. Please try again.";
            }
        }
        catch (JSException)
        {
            Error = "Failed to delete the account. Please try again.";
        }
        catch (HttpRequestException)
        {
            Error = "Failed to delete the account. Please try again.";
        }
        finally
        {
            Deleting = false;
        }
    }
}
