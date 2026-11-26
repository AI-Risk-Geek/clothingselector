using Microsoft.JSInterop;

namespace OutfitFinder.UI.Services;

/// <summary>Lazy wrapper around wwwroot/js/outfit.js.</summary>
public sealed class OutfitJs(IJSRuntime js, OutfitJsOptions? options = null) : IAsyncDisposable, IDisposable
{
    /// <summary>Synchronous containers (e.g. tests) can dispose too; the JS module is released with the circuit.</summary>
    public void Dispose() { }

    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import",
        (options?.ContentRoot ?? "./_content/") + "OutfitFinder.UI/js/outfit.js").AsTask();

    public async ValueTask<T> Invoke<T>(string fn, params object?[] args) => await (await Module).InvokeAsync<T>(fn, args);

    public async ValueTask Invoke(string fn, params object?[] args) => await (await Module).InvokeVoidAsync(fn, args);

    public async ValueTask DisposeAsync()
    {
        if (_module is { IsCompletedSuccessfully: true })
        {
            try { await (await _module).DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}

public sealed record BrowserInfo(string Name, bool Supported, bool IsMobile);

/// <summary>Browser extensions can't serve folders starting with "_", so they use "./content/" instead of "./_content/".</summary>
public sealed record OutfitJsOptions(string ContentRoot);
