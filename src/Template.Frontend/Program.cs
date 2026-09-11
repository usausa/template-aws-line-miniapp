using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using Template.Frontend.Components;
using Template.Frontend.Services;
using Template.Frontend.Settings;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Settings (wwwroot/appsettings.json is loaded automatically by CreateDefault). Every value is
// public by design; the security boundary is the app JWT, not the configuration (SPEC 7.5).
var setting = builder.Configuration.GetSection("App").Get<AppSetting>() ?? new AppSetting();
builder.Services.AddSingleton(setting);

// One HttpClient pointed at the API base. In production this is the app's own origin (CloudFront
// /api), so calls are same-origin; in local dev it is the dev distribution, reached cross-origin
// with the dev-only CORS allowance.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(setting.ApiEndpoint.TrimEnd('/') + "/"),
});

builder.Services.AddScoped<LiffService>();
builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
