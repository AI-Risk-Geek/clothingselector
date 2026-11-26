using Blazor.BrowserExtension;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OutfitFinder.Extension;
using OutfitFinder.UI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.UseBrowserExtension(_ =>
{
    builder.RootComponents.Add<App>("#app");
    builder.RootComponents.Add<HeadOutlet>("head::after");
});

var settings = new ExtensionSettings(
    builder.Configuration["ApiBaseUrl"] ?? throw new InvalidOperationException("ApiBaseUrl missing from appsettings.json"),
    builder.Configuration["IdentityAuthority"] ?? throw new InvalidOperationException("IdentityAuthority missing from appsettings.json"),
    builder.Configuration["WebsiteUrl"] ?? throw new InvalidOperationException("WebsiteUrl missing from appsettings.json"));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(new OutfitJsOptions("./content/"));
builder.Services.AddScoped<OutfitJs>();
builder.Services.AddScoped<ExtJs>();
builder.Services.AddScoped<ExtensionVisitor>();
builder.Services.AddScoped<IVisitorIdProvider>(sp => sp.GetRequiredService<ExtensionVisitor>());
builder.Services.AddScoped(sp => new OutfitApiClient(
    new HttpClient(new ExtensionAuthHandler(sp.GetRequiredService<ExtJs>(), settings)) { BaseAddress = new Uri(settings.ApiBaseUrl.TrimEnd('/') + "/") },
    sp.GetRequiredService<IVisitorIdProvider>()));

await builder.Build().RunAsync();
