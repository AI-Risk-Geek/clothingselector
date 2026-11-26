using System.Runtime.CompilerServices;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using OutfitFinder.Core.Abstractions;

namespace OutfitFinder.Infrastructure.Storage;

/// <summary>Azure Blob Storage (Azurite locally).</summary>
public sealed class AzureBlobStore : IBlobStore
{
    private readonly BlobContainerClient _container;
    private int _created;

    public AzureBlobStore(string connectionString, string container)
    {
        _container = new BlobContainerClient(connectionString, container);
    }

    private async Task EnsureAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _created, 1) == 0)
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
    }

    public async Task SaveAsync(string path, byte[] content, string contentType, CancellationToken ct)
    {
        await EnsureAsync(ct);
        await _container.GetBlobClient(path).UploadAsync(new BinaryData(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
    }

    public async Task<byte[]?> ReadAsync(string path, CancellationToken ct)
    {
        await EnsureAsync(ct);
        var blob = _container.GetBlobClient(path);
        try { return (await blob.DownloadContentAsync(ct)).Value.Content.ToArray(); }
        catch (Azure.RequestFailedException e) when (e.Status == 404) { return null; }
    }

    public async Task DeleteAsync(string path, CancellationToken ct)
    {
        await EnsureAsync(ct);
        await _container.DeleteBlobIfExistsAsync(path, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
    }

    public async IAsyncEnumerable<string> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureAsync(ct);
        await foreach (var b in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct)) yield return b.Name;
    }
}

/// <summary>Local folder store for running the site without Azurite. Never used in Azure.</summary>
public sealed class FileSystemBlobStore(string root) : IBlobStore
{
    private string Full(string path)
    {
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Bad blob path");
        return full;
    }

    public async Task SaveAsync(string path, byte[] content, string contentType, CancellationToken ct)
    {
        var f = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        await File.WriteAllBytesAsync(f, content, ct);
    }

    public async Task<byte[]?> ReadAsync(string path, CancellationToken ct) =>
        File.Exists(Full(path)) ? await File.ReadAllBytesAsync(Full(path), ct) : null;

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        if (File.Exists(Full(path))) File.Delete(Full(path));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        if (!Directory.Exists(root)) yield break;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.StartsWith(prefix, StringComparison.Ordinal)) yield return rel;
        }
    }
}

public static class BlobPaths
{
    public static string Pending(Guid searchId) => $"pending/{searchId:N}.jpg";
    public static string Blurred(Guid searchId) => $"searches/{searchId:N}.jpg";
    public static string PatternPdf(Guid jobId, string kind) => $"patterns/{jobId:N}-{kind}.pdf";
}
