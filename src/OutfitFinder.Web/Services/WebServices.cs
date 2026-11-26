using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using OutfitFinder.UI.Services;

namespace OutfitFinder.Web.Services;

public static class VisitorCookies
{
    public const string Visitor = "gf_visitor";
    public const string Consent = "gf_consent";
}

/// <summary>
/// A small preview of the photo just uploaded in this tab, so "Finding your look" can show it before the blurred copy
/// exists. It lives only in this browser tab's circuit and is never stored or sent anywhere else.
/// </summary>
public sealed class UploadPreview
{
    private (Guid Id, string Url)? _last;
    public void Set(Guid searchId, string? url) => _last = url is null ? null : (searchId, url);
    public string? For(Guid searchId) => _last is { } l && l.Id == searchId ? l.Url : null;
}

/// <summary>Per-circuit visitor state. Set from the cookie when the page loads; ephemeral if cookies were declined.</summary>
public sealed class WebVisitorState : IVisitorIdProvider
{
    private Guid? _ephemeral;
    public Guid? PersistentId { get; set; }
    public string? ConsentChoice { get; set; }
    public bool ConsentDecided => ConsentChoice is "accepted" or "declined";

    public event Action? Changed;

    /// <summary>Raised when a page (e.g. Account) asks the layout to show the cookie choice again.</summary>
    public event Action? CookieSettingsRequested;
    public void OpenCookieSettings() => CookieSettingsRequested?.Invoke();

    /// <summary>Without consent we still need an id for this session's searches; it is never stored in a cookie.</summary>
    public ValueTask<Guid?> GetAsync() => ValueTask.FromResult<Guid?>(PersistentId ?? (_ephemeral ??= Guid.NewGuid()));

    public void Set(Guid? persistent, string? choice)
    {
        PersistentId = persistent;
        ConsentChoice = choice;
        Changed?.Invoke();
    }
}

public sealed record UserTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Server-side token store keyed by the user's subject id. Blazor Server circuits have no HttpContext,
/// so the access token captured at sign-in (and on every full page load) is kept here.
/// </summary>
public sealed class TokenStore(IHttpClientFactory httpFactory, IConfiguration config, ILogger<TokenStore> log)
{
    private readonly ConcurrentDictionary<string, UserTokens> _tokens = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public void Save(string sub, UserTokens tokens) => _tokens[sub] = tokens;

    public void Remove(string sub) => _tokens.TryRemove(sub, out _);

    public async Task<string?> GetAccessTokenAsync(string sub, CancellationToken ct)
    {
        if (!_tokens.TryGetValue(sub, out var t)) return null;
        if (t.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return t.AccessToken;
        if (t.RefreshToken is null) return null;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_tokens.TryGetValue(sub, out var again) && again.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return again.AccessToken;
            var http = httpFactory.CreateClient("identity");
            var res = await http.PostAsync($"{config["Identity:Authority"]!.TrimEnd('/')}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = t.RefreshToken,
                ["client_id"] = "web",
                ["client_secret"] = config["Identity:ClientSecret"]!,
            }), ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Token refresh failed with {Status}", (int)res.StatusCode);
                _tokens.TryRemove(sub, out _);
                return null;
            }
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var fresh = new UserTokens(json.GetProperty("access_token").GetString()!,
                json.TryGetProperty("refresh_token", out var r) ? r.GetString() : t.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32()));
            _tokens[sub] = fresh;
            return fresh.AccessToken;
        }
        finally { _refreshLock.Release(); }
    }
}

/// <summary>Adds the signed-in user's bearer token to Api calls made from the circuit.</summary>
public sealed class ApiAuthHandler(AuthenticationStateProvider auth, TokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var state = await auth.GetAuthenticationStateAsync();
        var sub = state.User.FindFirst("sub")?.Value;
        if (sub is not null && await tokens.GetAccessTokenAsync(sub, ct) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }

    // The inner handler is shared by every circuit; never dispose it with this wrapper.
    protected override void Dispose(bool disposing) { }
}
