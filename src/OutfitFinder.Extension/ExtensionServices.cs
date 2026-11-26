using System.Net.Http.Headers;
using Microsoft.JSInterop;
using OutfitFinder.UI.Services;

namespace OutfitFinder.Extension;

/// <summary>Where the extension talks to. Read from wwwroot/appsettings.json (rewritten per environment by CI).</summary>
public sealed record ExtensionSettings(string ApiBaseUrl, string IdentityAuthority, string WebsiteUrl, string ClientId = "extension");

/// <summary>Wrapper around wwwroot/js/ext.js (chrome.* APIs).</summary>
public sealed class ExtJs(IJSRuntime js)
{
    private Task<IJSObjectReference>? _module;
    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/ext.js").AsTask();

    public async ValueTask<T> Call<T>(string fn, params object?[] args) => await (await Module).InvokeAsync<T>(fn, args);
    public async ValueTask Call(string fn, params object?[] args) => await (await Module).InvokeVoidAsync(fn, args);
}

/// <summary>Anonymous use: a visitor id kept in chrome.storage.local.</summary>
public sealed class ExtensionVisitor(ExtJs ext) : IVisitorIdProvider
{
    private Guid? _id;
    public async ValueTask<Guid?> GetAsync() => _id ??= Guid.Parse(await ext.Call<string>("getVisitorId"));
}

/// <summary>Adds the signed-in user's token (refreshed when needed) to Api calls.</summary>
public sealed class ExtensionAuthHandler(ExtJs ext, ExtensionSettings settings) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (await ext.Call<string?>("getAccessToken", settings.IdentityAuthority, settings.ClientId) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}

/// <summary>A right-click (srcUrl) or popup upload (searchId) waiting for the side panel.</summary>
public sealed record PendingRequest(string? SrcUrl, string? PageUrl, int TabId, int FrameId, Guid? SearchId);
