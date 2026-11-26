using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Entities;

namespace OutfitFinder.Core.Abstractions;

/// <summary>One adapter per AI provider. Instructions + JSON schema come from <see cref="AiResources"/>.</summary>
public interface IOutfitAnalyzer
{
    AiProviderKind Kind { get; }

    /// <summary>Find every item in the photo, the faces, the flags, and matches for each item.</summary>
    Task<AnalyzerResult<OutfitAnalysis>> AnalyzeAsync(byte[] jpeg, AiRuntimeSettings settings, CancellationToken ct);

    /// <summary>Find products for one item (fix-a-box re-search, or a materials line).</summary>
    Task<AnalyzerResult<ItemSearchResult>> FindProductsAsync(string category, string description, byte[]? cropJpeg, AiRuntimeSettings settings, CancellationToken ct);
}

/// <summary>Effective AI settings for one call: the admin row if present, otherwise configuration.</summary>
public sealed record AiRuntimeSettings(
    AiProviderKind Provider,
    string Model,
    string? Deployment,
    string? Endpoint,
    string ApiKey,
    bool WebSearchEnabled,
    int TimeoutSeconds)
{
    /// <summary>Throws with a clear message when the provider is not usable. No silent fallbacks.</summary>
    public void EnsureConfigured()
    {
        if (Provider == AiProviderKind.Demo) return;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Model) && string.IsNullOrWhiteSpace(Deployment)) missing.Add("AI:Model");
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("AI:ApiKey");
        if (Provider == AiProviderKind.AzureAiFoundry && string.IsNullOrWhiteSpace(Endpoint)) missing.Add("AI:Endpoint");
        if (missing.Count > 0)
            throw new AiNotConfiguredException($"AI provider {Provider} is not configured: missing {string.Join(", ", missing)}.");
    }
}

public sealed class AiNotConfiguredException(string message) : InvalidOperationException(message);

public interface IAiSettingsProvider
{
    Task<AiRuntimeSettings> GetAsync(CancellationToken ct);
}

public interface IAnalyzerFactory
{
    IOutfitAnalyzer For(AiProviderKind kind);
}

public sealed record ModerationResult(bool Allowed, string? Reason, int MaxSeverity);

public interface IImageModerator
{
    Task<ModerationResult> CheckAsync(byte[] image, CancellationToken ct);
}

public interface IBlobStore
{
    Task SaveAsync(string path, byte[] content, string contentType, CancellationToken ct);
    Task<byte[]?> ReadAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    IAsyncEnumerable<string> ListAsync(string prefix, CancellationToken ct);
}

public sealed record ProcessedImage(byte[] Jpeg, int Width, int Height, string Sha256);

public interface IImageProcessor
{
    /// <summary>Checks the file signature (jpg/png/webp only), re-encodes to JPEG at max 2048 px, strips metadata.</summary>
    ProcessedImage Normalise(byte[] upload);
    byte[] BlurRegions(byte[] jpeg, IEnumerable<NormalizedBox> faces, double padding = 0.25);
    byte[] Crop(byte[] jpeg, NormalizedBox box);
}

public interface ILinkChecker
{
    /// <summary>Returns the set of URLs that answered with a non-error status within the timeout.</summary>
    Task<IReadOnlySet<string>> FindLiveAsync(IEnumerable<string> urls, CancellationToken ct);
}

public interface IOutboundLinkSafety
{
    /// <summary>Returns the URLs flagged as unsafe (malware, phishing).</summary>
    Task<IReadOnlySet<string>> FindUnsafeAsync(IEnumerable<string> urls, CancellationToken ct);
}

public interface ISafeImageFetcher
{
    /// <summary>Fetches an image URL with SSRF protections. Throws <see cref="ImageFetchException"/> on any refusal.</summary>
    Task<byte[]> FetchAsync(string url, CancellationToken ct);
}

public sealed class ImageFetchException(string message) : Exception(message);

public sealed class InvalidImageException(string message) : Exception(message);

public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct);
}
