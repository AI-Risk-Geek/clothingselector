using Microsoft.EntityFrameworkCore;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using static OutfitFinder.Api.Endpoints.SearchEndpoints;

namespace OutfitFinder.Api.Services;

/// <summary>Fix-a-box: remove unticked items, and re-search only the items whose box moved or that are new.</summary>
public sealed class ItemAdjuster(
    OutfitDbContext db, IBlobStore blobs, IImageProcessor images, IAiSettingsProvider settingsProvider, IAnalyzerFactory analyzers,
    ILinkChecker links, IOutboundLinkSafety linkSafety, UsageGuard guard, UrlSigner signer)
{
    public async Task<IResult> AdjustAsync(Caller caller, Guid searchId, AdjustItemsRequest req, CancellationToken ct)
    {
        var search = await caller.Own(db.Searches).Include(s => s.Items).ThenInclude(i => i.Matches).FirstOrDefaultAsync(s => s.Id == searchId, ct);
        if (search is null) return Error(404, "We couldn't find that look.");
        if (search.Status != SearchStatus.Completed || search.BlurredBlobPath is null) return Error(409, "This look is still being searched.");

        var check = await guard.CheckAsync(caller, ct);
        if (!check.Allowed) return Error(check.Status, check.Message);

        var photo = await blobs.ReadAsync(search.BlurredBlobPath, ct);
        if (photo is null) return Error(404, "That photo has been deleted.");

        // An edited look no longer matches what the AI found for this photo: take it out of the image-hash cache.
        search.ImageHash = Infrastructure.Imaging.ImageProcessor.Sha256(System.Text.Encoding.UTF8.GetBytes($"edited:{search.ImageHash}:{search.Id}"));

        var keepIds = req.Items.Where(i => i.Id is not null).Select(i => i.Id!.Value).ToHashSet();
        db.Items.RemoveRange(search.Items.Where(i => !keepIds.Contains(i.Id)));

        var toSearch = new List<(OutfitItem Item, BoxDto Box)>();
        var number = 1;
        foreach (var adj in req.Items)
        {
            var box = Clamp(adj.Box);
            if (adj.Id is { } id && search.Items.FirstOrDefault(i => i.Id == id) is { } existing)
            {
                existing.Number = number++;
                var moved = Math.Abs(existing.BoxX - box.X) + Math.Abs(existing.BoxY - box.Y) + Math.Abs(existing.BoxW - box.W) + Math.Abs(existing.BoxH - box.H) > 0.01;
                if (!moved) continue;
                (existing.BoxX, existing.BoxY, existing.BoxW, existing.BoxH, existing.IsUserAdjusted) = (box.X, box.Y, box.W, box.H, true);
                toSearch.Add((existing, box));
            }
            else
            {
                var category = string.IsNullOrWhiteSpace(adj.Category) ? "Other" : adj.Category.Trim();
                var item = new OutfitItem
                {
                    Id = Guid.NewGuid(), OutfitSearchId = search.Id, Number = number++, Category = category, Name = category,
                    Description = "", BoxX = box.X, BoxY = box.Y, BoxW = box.W, BoxH = box.H, Confidence = 1, IsUserAdjusted = true,
                    Makeable = category is "Top" or "Bottoms",
                    MakeItJson = $$"""{"makeable":{{(category is "Top" or "Bottoms" ? "true" : "false")}},"garmentType":"{{(category == "Top" ? "tshirt" : category == "Bottoms" ? "trousers" : "other")}}"}""",
                };
                db.Items.Add(item);
                search.Items.Add(item);
                toSearch.Add((item, box));
            }
        }

        if (toSearch.Count > 0)
        {
            var settings = await settingsProvider.GetAsync(ct);
            var analyzer = analyzers.For(settings.Provider);
            var results = await Task.WhenAll(toSearch.Select(async t =>
            {
                var crop = images.Crop(photo, new NormalizedBox { X = t.Box.X, Y = t.Box.Y, W = t.Box.W, H = t.Box.H });
                var description = string.IsNullOrWhiteSpace(t.Item.Description) ? $"The {t.Item.Category.ToLowerInvariant()} shown in this photo" : t.Item.Description;
                return (t.Item, Result: await analyzer.FindProductsAsync(t.Item.Category, description, crop, settings, ct));
            }));
            var urls = results.SelectMany(r => r.Result.Value.Matches).Select(m => m.Url).ToList();
            var live = await links.FindLiveAsync(urls, ct);
            var bad = await linkSafety.FindUnsafeAsync(urls, ct);
            foreach (var (item, result) in results)
            {
                db.Matches.RemoveRange(item.Matches);
                var matches = SearchPipeline.ToMatches(item.Id, result.Value.Matches, live, bad);
                db.Matches.AddRange(matches);
                item.Matches = matches;
                search.CostUsd += result.CostUsd;
            }
        }

        await db.SaveChangesAsync(ct);
        var fresh = await db.Searches.AsNoTracking().Include(s => s.Items).ThenInclude(i => i.Matches).FirstAsync(s => s.Id == searchId, ct);
        return Results.Ok(fresh.ToDto(signer.SearchImage(fresh.Id)));
    }

    private static BoxDto Clamp(BoxDto b)
    {
        var x = Math.Clamp(b.X, 0, 0.98); var y = Math.Clamp(b.Y, 0, 0.98);
        return new BoxDto(x, y, Math.Clamp(b.W, 0.02, 1 - x), Math.Clamp(b.H, 0.02, 1 - y));
    }
}
