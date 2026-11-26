using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using OutfitFinder.Core.Contracts;
using OutfitFinder.UI.Services;
using OutfitFinder.Web.Components;
using OutfitFinder.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
// Most local failures here are the "MySQL (Docker)" profile run without user-secrets, so the message names the quick fix.
string Require(string key) => config[key] is { Length: > 0 } v ? v : throw new InvalidOperationException(
    $"Configuration value '{key}' is required. To try the app without any keys, start the solution with the " +
    "\"Demo (SQLite, no keys)\" launch profile (it starts the sign-in server, API and website together). " +
    "For the \"MySQL (Docker)\" profile, set it with dotnet user-secrets as shown in README.md; in Azure it comes from Key Vault.");

if (!string.IsNullOrEmpty(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpClient("identity");

// Legal pages name the providers and contact addresses; a launch with blanks would publish an unfinished policy.
var missingLegal = OutfitFinder.UI.Legal.LegalDocuments.Configure(k => config[k]);
if (missingLegal.Count > 0 && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Legal pages need these settings before launch: " + string.Join(", ", missingLegal));

var authority = Require("Identity:Authority");
var apiBase = new Uri(Require("Api:BaseUrl").TrimEnd('/') + "/");

builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = "of_session";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = authority;
        o.ClientId = "web";
        o.ClientSecret = Require("Identity:ClientSecret");
        o.ResponseType = "code";
        o.UsePkce = true;
        o.SaveTokens = true;
        o.MapInboundClaims = false;
        o.GetClaimsFromUserInfoEndpoint = true;
        o.Scope.Clear();
        foreach (var s in new[] { "openid", "profile", "roles", "outfit.api", "offline_access" }) o.Scope.Add(s);
        o.ClaimActions.MapJsonKey("role", "role");
        o.TokenValidationParameters.NameClaimType = "name";
        o.TokenValidationParameters.RoleClaimType = "role";
        o.Events.OnTokenResponseReceived = ctx => Task.CompletedTask;
        o.Events.OnTicketReceived = async ctx =>
        {
            // Keep the API token server-side for the Blazor circuit, then move this device's looks into the account.
            var props = ctx.Properties!;
            var sub = ctx.Principal!.FindFirstValue("sub")!;
            var access = props.GetTokenValue("access_token")!;
            var store = ctx.HttpContext.RequestServices.GetRequiredService<TokenStore>();
            store.Save(sub, new UserTokens(access, props.GetTokenValue("refresh_token"),
                DateTimeOffset.TryParse(props.GetTokenValue("expires_at"), out var exp) ? exp : DateTimeOffset.UtcNow.AddMinutes(55)));
            if (Guid.TryParse(ctx.Request.Cookies[VisitorCookies.Visitor], out var visitor))
            {
                using var http = new HttpClient { BaseAddress = apiBase };
                http.DefaultRequestHeaders.Authorization = new("Bearer", access);
                try { (await http.PostAsync($"api/me/merge-visitor/{visitor}", null)).EnsureSuccessStatusCode(); }
                catch (HttpRequestException e) { ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>().LogError(e, "Merging visitor history failed"); }
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<TokenStore>();
builder.Services.AddScoped<WebVisitorState>();
builder.Services.AddScoped<UploadPreview>();
builder.Services.AddScoped<IVisitorIdProvider>(sp => sp.GetRequiredService<WebVisitorState>());
builder.Services.AddScoped<OutfitJs>();
var sharedApiHandler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
builder.Services.AddScoped(sp =>
{
    var handler = new ApiAuthHandler(sp.GetRequiredService<AuthenticationStateProvider>(), sp.GetRequiredService<TokenStore>()) { InnerHandler = sharedApiHandler };
    return new OutfitApiClient(new HttpClient(handler, disposeHandler: false) { BaseAddress = apiBase, Timeout = TimeSpan.FromMinutes(5) },
        sp.GetRequiredService<IVisitorIdProvider>());
});
builder.Services.AddSingleton(sp => new SiteSettings(config["Extension:Id"], config["Extension:WebStoreUrl"], config.GetValue("Extension:ManualDownloadEnabled", true)));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// The extension zip is added to wwwroot/extension after the site is built (by CI or scripts/package-extension.ps1).
// MapStaticAssets only serves files known at build time, so this folder gets its own file server.
var extensionFolder = Path.Combine(app.Environment.WebRootPath, "extension");
Directory.CreateDirectory(extensionFolder);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(extensionFolder),
    RequestPath = "/extension",
    ServeUnknownFileTypes = false,
});

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapGet("/signin", (string? returnUrl) => Results.Challenge(
    new AuthenticationProperties { RedirectUri = SafeReturn(returnUrl) }, [OpenIdConnectDefaults.AuthenticationScheme]));

app.MapPost("/signout", async (HttpContext ctx, TokenStore store) =>
{
    if (ctx.User.FindFirstValue("sub") is { } sub) store.Remove(sub);
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIdConnectDefaults.AuthenticationScheme]);
});

// After account deletion: drop the local session (the identity account no longer exists).
app.MapGet("/signout-complete", async (HttpContext ctx, TokenStore store) =>
{
    if (ctx.User.FindFirstValue("sub") is { } sub) store.Remove(sub);
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

// Cookie choice: the visitor cookie is set only after consent.
app.MapPost("/consent/cookies", ([FromBody] CookieChoice choice, HttpContext ctx) =>
{
    var opts = new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true };
    ctx.Response.Cookies.Append(VisitorCookies.Consent, choice.Accept ? "accepted" : "declined", opts);
    if (!choice.Accept)
    {
        ctx.Response.Cookies.Delete(VisitorCookies.Visitor);
        return Results.Ok(new { visitorId = (Guid?)null });
    }
    var id = Guid.TryParse(ctx.Request.Cookies[VisitorCookies.Visitor], out var existing) ? existing : Guid.NewGuid();
    ctx.Response.Cookies.Append(VisitorCookies.Visitor, id.ToString(), opts);
    return Results.Ok(new { visitorId = (Guid?)id });
}).DisableAntiforgery();

// Download my data: streamed through the site so the browser gets a normal file download.
app.MapGet("/account/export", async (HttpContext ctx, TokenStore store, IHttpClientFactory factory) =>
{
    using var http = new HttpClient { BaseAddress = apiBase };
    using var req = new HttpRequestMessage(HttpMethod.Get, "api/me/export");
    if (ctx.User.FindFirstValue("sub") is { } sub && await store.GetAccessTokenAsync(sub, ctx.RequestAborted) is { } token)
        req.Headers.Authorization = new("Bearer", token);
    if (Guid.TryParse(ctx.Request.Cookies[VisitorCookies.Visitor], out var visitor)) req.Headers.Add(Headers.VisitorId, visitor.ToString());
    using var res = await http.SendAsync(req, ctx.RequestAborted);
    if (!res.IsSuccessStatusCode) return Results.Problem("Export failed", statusCode: (int)res.StatusCode);
    return Results.File(await res.Content.ReadAsByteArrayAsync(ctx.RequestAborted), "application/zip", $"{OutfitFinder.Core.Brand.Name.Replace(' ', '-')}-my-data.zip");
});

// Extension release info for the install page (written by CI next to the zip).
app.MapGet("/extension/latest.json", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "extension", "latest.json");
    return File.Exists(path) ? Results.File(path, "application/json") : Results.NotFound();
});

app.Run();

static string SafeReturn(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") ? url : "/";

public sealed record CookieChoice(bool Accept);

public sealed record SiteSettings(string? ExtensionId, string? WebStoreUrl, bool ManualDownloadEnabled);

