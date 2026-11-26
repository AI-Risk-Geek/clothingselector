using Microsoft.EntityFrameworkCore;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Api.Services;

/// <summary>Deletes searches together with every blob that belongs to them (photos, pattern PDFs).</summary>
public sealed class DataDeleter(OutfitDbContext db, IBlobStore blobs)
{
    public async Task DeleteSearchesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var searches = await db.Searches.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        var itemIds = await db.Items.Where(i => ids.Contains(i.OutfitSearchId)).Select(i => i.Id).ToListAsync(ct);
        var jobs = await db.PatternJobs.Where(j => itemIds.Contains(j.OutfitItemId)).ToListAsync(ct);

        foreach (var path in searches.SelectMany(s => new[] { s.BlurredBlobPath, s.PendingBlobPath })
                     .Concat(jobs.SelectMany(j => new[] { j.PdfBlobPath, j.A0PdfBlobPath })).OfType<string>())
            await blobs.DeleteAsync(path, ct);
        foreach (var j in jobs)
            await foreach (var p in blobs.ListAsync($"patterns/{j.Id:N}", ct)) await blobs.DeleteAsync(p, ct);

        // One delete: the database cascades Search → Items → (Matches, PatternJobs → MaterialLines).
        // Deleting PatternJobs explicitly as well would race the cascade and fail the concurrency check.
        db.Searches.RemoveRange(searches);
        await db.SaveChangesAsync(ct);
    }
}
