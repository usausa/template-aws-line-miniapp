namespace Template.Frontend.Services;

using System.Net.Http.Headers;

// Calls the backend, carrying the app JWT as a bearer token. Two responsibilities beyond the plain
// HTTP:
//
//   1. Token acquisition - if no valid token is in memory, exchange the LINE ID token for one
//      (POST /api/auth/line) before the call.
//   2. 401 recovery - if a call comes back 401 (e.g. the app JWT expired after 7 days), exchange
//      once and retry the call exactly once. The single-retry cap is essential: retrying in a loop
//      would bounce the user between the app and the LINE login page forever (SPEC 3.3).
public sealed class ApiClient
{
    private readonly HttpClient http;
    private readonly TokenStore store;
    private readonly LiffService liff;

    public ApiClient(HttpClient http, TokenStore store, LiffService liff)
    {
        this.http = http;
        this.store = store;
        this.liff = liff;
    }

    // Ensures a usable app token is in memory, exchanging via LIFF if needed. Returns false when a
    // re-login redirect is under way (the page is navigating; the caller should stop).
    public async Task<bool> EnsureTokenAsync()
    {
        if (store.HasValidToken)
        {
            return true;
        }

        return await ExchangeAsync();
    }

    public async Task<GetDataResult> GetDataAsync()
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "data"));
        if (response is null)
        {
            return new GetDataResult(ApiOutcome.Unauthorized, null);
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return new GetDataResult(ApiOutcome.NoContent, null);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new GetDataResult(ApiOutcome.Unauthorized, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new GetDataResult(ApiOutcome.Error, null);
        }

        var data = await response.Content.ReadFromJsonAsync(ApiSerializerContext.Default.DataResponse);
        return new GetDataResult(ApiOutcome.Ok, data);
    }

    public async Task<PutDataResult> PutDataAsync(string data, int version)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, "data")
        {
            Content = JsonContent.Create(new PutRequest(data, version), ApiSerializerContext.Default.PutRequest),
        });

        if (response is null)
        {
            return new PutDataResult(ApiOutcome.Unauthorized, 0);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Conflict:
                return new PutDataResult(ApiOutcome.Conflict, 0);
            case HttpStatusCode.RequestEntityTooLarge:
                return new PutDataResult(ApiOutcome.TooLarge, 0);
            case HttpStatusCode.Unauthorized:
                return new PutDataResult(ApiOutcome.Unauthorized, 0);
            default:
                break;
        }

        if (!response.IsSuccessStatusCode)
        {
            return new PutDataResult(ApiOutcome.Error, 0);
        }

        var body = await response.Content.ReadFromJsonAsync(ApiSerializerContext.Default.PutResponse);
        return new PutDataResult(ApiOutcome.Ok, body?.Version ?? (version + 1));
    }

    public async Task<ApiOutcome> DeleteAccountAsync()
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, "account"));
        if (response is null)
        {
            return ApiOutcome.Unauthorized;
        }

        if (response.IsSuccessStatusCode)
        {
            return ApiOutcome.Ok;
        }

        return response.StatusCode == HttpStatusCode.Unauthorized ? ApiOutcome.Unauthorized : ApiOutcome.Error;
    }

    private async Task<bool> ExchangeAsync()
    {
        var idToken = await liff.GetFreshIdTokenAsync();
        if (idToken is null)
        {
            // getFreshIdToken kicked off a re-login redirect; nothing more to do here.
            return false;
        }

        using var response = await http.PostAsJsonAsync(
            "auth/line", new LoginRequest(idToken), ApiSerializerContext.Default.LoginRequest);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var login = await response.Content.ReadFromJsonAsync(ApiSerializerContext.Default.LoginResponse);
        if (login is null)
        {
            return false;
        }

        store.Set(login.Token, login.ExpiresIn);
        return true;
    }

    // Sends the request with the current token, retrying once through a fresh exchange on 401.
    // Returns null when no token could be obtained (a re-login redirect is in progress).
    private async Task<HttpResponseMessage?> SendAsync(Func<HttpRequestMessage> factory)
    {
        if (!await EnsureTokenAsync())
        {
            return null;
        }

        var response = await SendOnceAsync(factory);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        if (!await ExchangeAsync())
        {
            return null;
        }

        // Retry exactly once. Never loop.
        return await SendOnceAsync(factory);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> factory)
    {
        var request = factory();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", store.Token);
        return await http.SendAsync(request);
    }
}

// Outcome of an API call, mapped from the HTTP status so pages need not touch HttpResponseMessage.
public enum ApiOutcome
{
    Ok,
    NoContent,
    Unauthorized,
    Conflict,
    TooLarge,
    Error,
}

public sealed record GetDataResult(ApiOutcome Outcome, DataResponse? Data);

public sealed record PutDataResult(ApiOutcome Outcome, int Version);
