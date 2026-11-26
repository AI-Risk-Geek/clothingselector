using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Ai;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Imaging;
using OutfitFinder.Infrastructure.Storage;

namespace OutfitFinder.Api.Services;

/// <summary>Processes one claimed search: AI analysis → flags → face blur → link checks → save.</summary>
public sealed class SearchPipeline(
    OutfitDbContext db,
    IBlobStore blobs,
    IImageProcessor images,
    IAiSettingsProvider settingsProvider,
    IAnalyzerFactory analyzers,
    ILinkChecker links,
    IOutboundLinkSafety linkSafety,
    IServiceScopeFactory scopes,
    ILogger<SearchPipeline> log)
{
    public const int MaxAttempts = 3;

    public async Task ProcessAsync(Guid searchId, CancellationToken ct)
    {
        var search = await db.Searches.FirstAsync(s => s.Id == searchId, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            await SetStage(search, SearchStage.CheckingPhoto, ct);
            if (await TryUseCacheAsync(search, ct)) return;

            var pending = search.PendingBlobPath is null ? null : await blobs.ReadAsync(search.PendingBlobPath, ct);
            if (pending is null) throw new InvalidOperationException("The uploaded photo is no longer available.");

            await SetStage(search, SearchStage.FindingPieces, ct);
            var settings = await settingsProvider.GetAsync(ct);
            settings.EnsureConfigured();
            var analyzer = analyzers.For(settings.Provider);

            // One AI call finds the pieces and searches stores; advance the visible step while it runs.
            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var stageBump = BumpStageLaterAsync(search.Id, stageCts.Token);
            AnalyzerResult<OutfitAnalysis> result;
            try { result = await analyzer.AnalyzeAsync(pending, settings, ct); }
            finally { await stageCts.CancelAsync(); await stageBump; }

            await db.Entry(search).ReloadAsync(ct);
            search.Provider = result.Provider;
            search.Model = result.Model;
            search.CostUsd += result.CostUsd;
            var a = result.Value;

            if (a.Flags.AppearsToBeMinor || a.Flags.Inappropriate)
            {
                // Refuse and store nothing: no image, no items.
                db.ModerationEvents.Add(new ModerationEvent
                {
                    Id = Guid.NewGuid(), ImageHash = search.ImageHash, CreatedUtc = DateTime.UtcNow, VisitorId = search.VisitorId,
                    UserId = search.UserId, IpHash = search.IpHash,
                    Reason = a.Flags.AppearsToBeMinor ? "ai:appears-to-be-minor" : "ai:inappropriate",
                });
                await blobs.DeleteAsync(search.PendingBlobPath!, ct);
                search.PendingBlobPath = null;
                search.Status = SearchStatus.Rejected;
                search.Stage = SearchStage.Done;
                search.Error = a.Flags.AppearsToBeMinor
                    ? "We can't search photos that appear to show children."
                    : "This photo can't be searched because it may contain inappropriate content.";
                search.CompletedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            // Blur faces and keep only the blurred copy.
            var blurred = images.BlurRegions(pending, a.Faces);
            var blurredPath = BlobPaths.Blurred(search.Id);
            await blobs.SaveAsync(blurredPath, blurred, "image/jpeg", ct);
            await blobs.DeleteAsync(search.PendingBlobPath!, ct);
            search.BlurredBlobPath = blurredPath;
            search.PendingBlobPath = null;

            await SetStage(search, SearchStage.CheckingLinks, ct);
            var items = await BuildItemsAsync(search.Id, a.Items, ct);
            db.Items.AddRange(items);

            search.Status = SearchStatus.Completed;
            search.Stage = SearchStage.Done;
            search.Error = null;
            search.CompletedUtc = DateTime.UtcNow;
            search.DurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Search {Id} completed: {Items} items, {Links} links, {Cost:0.0000} USD, {Ms} ms",
                search.Id, items.Count, items.Sum(i => i.Matches.Count), search.CostUsd, search.DurationMs);
        }
        catch (AiNotConfiguredException e)
        {
            log.LogCritical(e, "AI provider is not configured. Search {Id} failed.", searchId);
            await FailAsync(search, "Searching isn't set up yet. " + e.Message, final: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // host shutting down: the row stays Running and is reclaimed after restart
        }
        catch (Exception e) when (e is AiResponseException or HttpRequestException or JsonException or InvalidOperationException or TimeoutException)
        {
            log.LogError(e, "Search {Id} attempt {Attempt} failed", searchId, search.AttemptCount);
            var final = search.AttemptCount >= MaxAttempts || e.Message.Contains("no longer available");
            await FailAsync(search, e is AiResponseException ? e.Message + " Please try again." : "We couldn't finish this search. Please try again.", final, ct);
        }
    }

    internal async Task<List<OutfitItem>> BuildItemsAsync(Guid searchId, IReadOnlyList<AnalyzedItem> analyzed, CancellationToken ct)
    {
        var allUrls = analyzed.SelectMany(i => i.Matches).Select(m => m.Url).ToList();
        var liveTask = links.FindLiveAsync(allUrls, ct);
        var unsafeTask = linkSafety.FindUnsafeAsync(allUrls, ct);
        var live = await liveTask; var bad = await unsafeTask;

        var items = new List<OutfitItem>();
        var n = 1;
        foreach (var ai in analyzed)
        {
            var item = new OutfitItem
            {
                Id = Guid.NewGuid(), OutfitSearchId = searchId, Number = n++,
                Category = Trim(ai.Category, 64), Name = Trim(string.IsNullOrWhiteSpace(ai.Name) ? ai.Category : ai.Name, 128),
                Description = Trim(ai.Description, 1000), BrandHints = ai.BrandHints is null ? null : Trim(ai.BrandHints, 256),
                BoxX = ai.Box.X, BoxY = ai.Box.Y, BoxW = ai.Box.W, BoxH = ai.Box.H, Confidence = ai.Confidence,
                Makeable = ai.MakeIt.Makeable, MakeItJson = JsonSerializer.Serialize(ai.MakeIt, AnalysisJson.Options),
            };
            item.Matches = ToMatches(item.Id, ai.Matches, live, bad);
            items.Add(item);
        }
        return items;
    }

    internal static List<ItemMatch> ToMatches(Guid itemId, IEnumerable<ProductMatch> matches, IReadOnlySet<string> live, IReadOnlySet<string> unsafeUrls)
    {
        var rank = 0;
        return matches.Where(m => live.Contains(m.Url) && !unsafeUrls.Contains(m.Url))
            .OrderBy(m => m.MatchType == "exact" ? 0 : 1)
            .Select(m => new ItemMatch
            {
                Id = Guid.NewGuid(), OutfitItemId = itemId, Store = Trim(m.Store, 128), Title = Trim(m.Title, 300),
                Price = m.Price, Currency = m.Currency?.ToUpperInvariant(), Url = m.Url, ImageUrl = m.ImageUrl,
                MatchType = m.MatchType == "exact" ? MatchKind.Exact : MatchKind.Similar, Rank = rank++, LinkVerified = true,
            }).ToList();
    }

    /// <summary>Same photo searched in the last 7 days: copy its results instead of paying for a new AI call.</summary>
    private async Task<bool> TryUseCacheAsync(OutfitSearch search, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var cached = await db.Searches.AsNoTracking().Include(s => s.Items).ThenInclude(i => i.Matches)
            .Where(s => s.Id != search.Id && s.ImageHash == search.ImageHash && s.Status == SearchStatus.Completed
                        && s.CreatedUtc >= since && s.BlurredBlobPath != null)
            .OrderByDescending(s => s.CreatedUtc).FirstOrDefaultAsync(ct);
        if (cached is null || cached.Items.Count == 0) return false;
        var blurred = await blobs.ReadAsync(cached.BlurredBlobPath!, ct);
        if (blurred is null) return false;

        var path = BlobPaths.Blurred(search.Id);
        await blobs.SaveAsync(path, blurred, "image/jpeg", ct);
        if (search.PendingBlobPath is not null) await blobs.DeleteAsync(search.PendingBlobPath, ct);
        search.PendingBlobPath = null;
        search.BlurredBlobPath = path;
        search.Provider = cached.Provider;
        search.Model = cached.Model + " (cached)";
        foreach (var i in cached.Items)
        {
            var itemId = Guid.NewGuid();
            db.Items.Add(new OutfitItem
            {
                Id = itemId, OutfitSearchId = search.Id, Number = i.Number, Category = i.Category, Name = i.Name, Description = i.Description,
                BrandHints = i.BrandHints, BoxX = i.BoxX, BoxY = i.BoxY, BoxW = i.BoxW, BoxH = i.BoxH, Confidence = i.Confidence,
                Makeable = i.Makeable, MakeItJson = i.MakeItJson,
                Matches = i.Matches.Select(m => new ItemMatch
                {
                    Id = Guid.NewGuid(), OutfitItemId = itemId, Store = m.Store, Title = m.Title, Price = m.Price, Currency = m.Currency,
                    Url = m.Url, ImageUrl = m.ImageUrl, MatchType = m.MatchType, Rank = m.Rank, LinkVerified = m.LinkVerified,
                }).ToList(),
            });
        }
        search.Status = SearchStatus.Completed;
        search.Stage = SearchStage.Done;
        search.CompletedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Search {Id} served from cache of {Cached}", search.Id, cached.Id);
        return true;
    }

    private async Task BumpStageLaterAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
            await using var scope = scopes.CreateAsyncScope();
            var other = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
            var row = await other.Searches.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (row is { Stage: SearchStage.FindingPieces })
            {
                row.Stage = SearchStage.SearchingStores;
                await other.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (DbUpdateConcurrencyException) { }
    }

    private async Task SetStage(OutfitSearch s, SearchStage stage, CancellationToken ct)
    {
        s.Stage = stage;
        await db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(OutfitSearch s, string message, bool final, CancellationToken ct)
    {
        await db.Entry(s).ReloadAsync(ct);
        if (final)
        {
            if (s.PendingBlobPath is not null) await blobs.DeleteAsync(s.PendingBlobPath, ct);
            s.PendingBlobPath = null;
            s.Status = SearchStatus.Failed;
            s.Stage = SearchStage.Done;
            s.Error = message.Length > 500 ? message[..500] : message;
            s.CompletedUtc = DateTime.UtcNow;
        }
        else
        {
            s.Status = SearchStatus.Pending; // retried by the worker
            s.Stage = SearchStage.Queued;
            s.ClaimedUtc = null;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
