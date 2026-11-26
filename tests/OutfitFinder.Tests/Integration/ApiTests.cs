using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OutfitFinder.Api.Services;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Storage;
using OutfitFinder.UI.Services;

namespace OutfitFinder.Tests.Integration;

public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public ApiTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Photo_goes_in_and_items_with_verified_links_come_out()
    {
        var c = _f.Visitor();
        var res = await c.PostAsync("api/searches", ApiFactory.Photo());
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var id = (await res.Content.ReadFromJsonAsync<CreatedSearchDto>())!.Id;

        var s = await ApiFactory.WaitForFinish(c, id);
        Assert.Equal(SearchStatus.Completed, s.Status);
        Assert.Equal(5, s.Items.Count);
        Assert.NotNull(s.ImageUrl);
        Assert.All(s.Items.SelectMany(i => i.Matches), m => Assert.StartsWith("https://", m.Url));

        // Only the blurred copy is stored; this search's pending upload is gone. (Only this search's file is checked:
        // other tests' searches may still be passing through the shared pending folder.)
        Assert.False(File.Exists(Path.Combine(_f.BlobRoot, "pending", $"{id:N}.jpg")));
        Assert.True(File.Exists(Path.Combine(_f.BlobRoot, "searches", $"{id:N}.jpg")));

        // The signed image link works without auth headers; a tampered one doesn't.
        var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(new Uri(s.ImageUrl!).PathAndQuery)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anon.GetAsync(new Uri(s.ImageUrl!).PathAndQuery.Replace("sig=", "sig=0"))).StatusCode);
    }

    [Fact]
    public async Task Another_visitor_cannot_see_my_look()
    {
        var me = _f.Visitor();
        var id = (await (await me.PostAsync("api/searches", ApiFactory.Photo())).Content.ReadFromJsonAsync<CreatedSearchDto>())!.Id;
        var other = _f.Visitor();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"api/searches/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/searches/{id}")).StatusCode);
    }

    [Fact]
    public async Task Non_image_upload_is_refused()
    {
        var res = await _f.Visitor().PostAsync("api/searches", ApiFactory.Photo("not an image"u8.ToArray()));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
        Assert.Contains("JPG, PNG or WebP", (await res.Content.ReadFromJsonAsync<ApiError>())!.Message);
    }

    [Fact]
    public async Task Ssrf_targets_are_refused_for_url_searches()
    {
        var res = await _f.Visitor().PostAsJsonAsync("api/searches/from-url", new SearchFromUrlRequest("https://169.254.169.254/latest/meta-data", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_return_401_without_a_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.Visitor().DeleteAsync("api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.Visitor().GetAsync("api/admin/ai")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.User("not-admin").GetAsync("api/admin/ai")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.User("boss", "Admin").GetAsync("api/admin/ai")).StatusCode);
    }

    [Fact]
    public async Task Worker_reclaims_a_job_left_running_by_a_restart()
    {
        var id = Guid.NewGuid();
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStore>();
            var img = scope.ServiceProvider.GetRequiredService<IImageProcessor>().Normalise(await File.ReadAllBytesAsync(ApiFactory.SamplePath));
            await blobs.SaveAsync(BlobPaths.Pending(id), img.Jpeg, "image/jpeg", default);
            var visitor = Guid.NewGuid();
            db.Searches.Add(new OutfitSearch
            {
                Id = id, VisitorId = visitor, ImageHash = Guid.NewGuid().ToString("N"), PendingBlobPath = BlobPaths.Pending(id),
                Status = SearchStatus.Running, Stage = SearchStage.FindingPieces, AttemptCount = 1,
                ClaimedUtc = DateTime.UtcNow.AddMinutes(-10), RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var s = await ApiFactory.WaitForFinish(_f.Visitor(visitor), id);
            Assert.Equal(SearchStatus.Completed, s.Status);
        }
        await using var check = _f.Services.CreateAsyncScope();
        Assert.Equal(2, (await check.ServiceProvider.GetRequiredService<OutfitDbContext>().Searches.SingleAsync(x => x.Id == id)).AttemptCount);
    }

    [Fact]
    public async Task Retention_deletes_back_dated_searches_and_their_blobs()
    {
        var oldId = Guid.NewGuid(); var newId = Guid.NewGuid();
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStore>();
            foreach (var (id, age) in new[] { (oldId, 91), (newId, 10) })
            {
                await blobs.SaveAsync(BlobPaths.Blurred(id), [1, 2, 3], "image/jpeg", default);
                db.Searches.Add(new OutfitSearch { Id = id, ImageHash = "r" + id, BlurredBlobPath = BlobPaths.Blurred(id), Status = SearchStatus.Completed, RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow.AddDays(-age) });
            }
            await db.SaveChangesAsync();
        }
        await _f.Services.GetRequiredService<RetentionWorker>().RunOnceAsync(DateTime.UtcNow, default);

        await using var verify = _f.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<OutfitDbContext>();
        Assert.False(await vdb.Searches.AnyAsync(s => s.Id == oldId));
        Assert.True(await vdb.Searches.AnyAsync(s => s.Id == newId));
        Assert.False(File.Exists(Path.Combine(_f.BlobRoot, "searches", $"{oldId:N}.jpg")));
        Assert.True(File.Exists(Path.Combine(_f.BlobRoot, "searches", $"{newId:N}.jpg")));
    }

    [Fact]
    public async Task Export_then_delete_account_leaves_nothing_behind()
    {
        var visitor = Guid.NewGuid();
        var user = "user-" + Guid.NewGuid().ToString("N")[..8];
        var c = _f.User(user, visitorId: visitor);
        var anon = _f.Visitor(visitor);

        // Anonymous look, then sign in and merge it into the account.
        var id = (await (await anon.PostAsync("api/searches", ApiFactory.Photo())).Content.ReadFromJsonAsync<CreatedSearchDto>())!.Id;
        await ApiFactory.WaitForFinish(anon, id);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"api/me/merge-visitor/{visitor}", null)).StatusCode);
        // Same history from "another device" (same user, different visitor id).
        var otherDevice = _f.User(user, visitorId: Guid.NewGuid());
        Assert.Contains((await otherDevice.GetFromJsonAsync<List<SearchSummaryDto>>("api/searches", OutfitApiClient.Json))!, s => s.Id == id);

        // Pattern + measurements so every kind of data exists.
        var search = await ApiFactory.WaitForFinish(c, id);
        var shirt = search.Items.First(i => i.Makeable);
        var pattern = await (await c.PostAsJsonAsync($"api/items/{shirt.Id}/pattern", new PatternRequest("M", null, null))).Content.ReadFromJsonAsync<PatternDto>(OutfitApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().GetAsync(new Uri(pattern!.TiledLetterUrl).PathAndQuery)).StatusCode);
        await c.PutAsJsonAsync("api/me/measurements", new MeasurementProfileDto(null, "Me", new() { ["bust"] = 92 }));

        var zipBytes = await c.GetByteArrayAsync("api/me/export");
        using (var zip = new ZipArchive(new MemoryStream(zipBytes)))
        {
            Assert.NotNull(zip.GetEntry("data.json"));
            Assert.NotNull(zip.GetEntry($"images/{id:N}.jpg"));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("api/me")).StatusCode);
        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
        Assert.False(await db.Searches.AnyAsync(s => s.UserId == user || s.Id == id));
        Assert.False(await db.MeasurementProfiles.AnyAsync(p => p.UserId == user));
        Assert.False(await db.PatternJobs.AnyAsync(j => j.Id == pattern.Id));
        Assert.False(File.Exists(Path.Combine(_f.BlobRoot, "searches", $"{id:N}.jpg")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_f.BlobRoot, "patterns"), $"{pattern.Id:N}*"));
    }

    [Fact]
    public async Task Fix_a_box_re_searches_only_changed_items()
    {
        var c = _f.Visitor();
        var id = (await (await c.PostAsync("api/searches", ApiFactory.Photo())).Content.ReadFromJsonAsync<CreatedSearchDto>())!.Id;
        var s = await ApiFactory.WaitForFinish(c, id);
        var keep = s.Items.Take(2).ToList();
        var moved = keep[1] with { Box = keep[1].Box with { X = keep[1].Box.X + 0.05 } };
        var req = new AdjustItemsRequest([new(keep[0].Id, null, keep[0].Box), new(moved.Id, null, moved.Box), new(null, "Hat", new BoxDto(0.4, 0.0, 0.2, 0.08))]);
        var res = await c.PutAsJsonAsync($"api/searches/{id}/items", req, OutfitApiClient.Json);
        res.EnsureSuccessStatusCode();
        var updated = (await res.Content.ReadFromJsonAsync<SearchDto>(OutfitApiClient.Json))!;
        Assert.Equal(3, updated.Items.Count);
        Assert.False(updated.Items[0].IsUserAdjusted);
        Assert.True(updated.Items[1].IsUserAdjusted);
        Assert.Equal("Hat", updated.Items[2].Category);
        Assert.Equal(keep[0].Matches.Select(m => m.Id), updated.Items[0].Matches.Select(m => m.Id)); // untouched item kept its matches
    }

    [Fact]
    public async Task Rate_limit_and_budget_cap_are_enforced()
    {
        var visitor = Guid.NewGuid();
        var config = _f.Services.GetRequiredService<IConfiguration>();
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
            for (var i = 0; i < 3; i++)
                db.Searches.Add(new OutfitSearch { Id = Guid.NewGuid(), VisitorId = visitor, ImageHash = "x" + i, Status = SearchStatus.Completed, RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        config["Limits:SearchesPerVisitorPerDay"] = "3";
        try { Assert.Equal((HttpStatusCode)429, (await _f.Visitor(visitor).PostAsync("api/searches", ApiFactory.Photo())).StatusCode); }
        finally { config["Limits:SearchesPerVisitorPerDay"] = "50"; }

        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
            db.Searches.Add(new OutfitSearch { Id = Guid.NewGuid(), ImageHash = "cost", Status = SearchStatus.Completed, CostUsd = 5, RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        config["Limits:DailyBudgetUsd"] = "1";
        try
        {
            var res = await _f.Visitor().PostAsync("api/searches", ApiFactory.Photo());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
            Assert.Contains("paused", (await res.Content.ReadFromJsonAsync<ApiError>())!.Message);
        }
        finally { config["Limits:DailyBudgetUsd"] = "0"; }
    }
}

/// <summary>Moderation rejects: nothing stored, event recorded, and repeated rejections block the device.</summary>
public class ModerationTests : IDisposable
{
    private sealed class RejectAll : IImageModerator
    {
        public Task<ModerationResult> CheckAsync(byte[] image, CancellationToken ct) => Task.FromResult(new ModerationResult(false, "Sexual severity 6", 6));
    }

    private readonly ApiFactory _f = new() { Moderator = () => new RejectAll() };

    [Fact]
    public async Task Unsafe_image_is_rejected_and_nothing_is_stored()
    {
        var visitor = Guid.NewGuid();
        var c = _f.Visitor(visitor);
        var res = await c.PostAsync("api/searches", ApiFactory.Photo());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);

        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
        Assert.False(await db.Searches.AnyAsync(s => s.VisitorId == visitor));
        var ev = await db.ModerationEvents.SingleAsync(m => m.VisitorId == visitor);
        Assert.Equal(64, ev.ImageHash.Length);
        Assert.False(Directory.Exists(_f.BlobRoot) && Directory.EnumerateFiles(_f.BlobRoot, "*", SearchOption.AllDirectories).Any());

        // Two more rejections → uploads from this device are paused.
        await c.PostAsync("api/searches", ApiFactory.Photo());
        await c.PostAsync("api/searches", ApiFactory.Photo());
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("api/searches", ApiFactory.Photo())).StatusCode);
    }

    public void Dispose() => _f.Dispose();
}

/// <summary>No AI provider configured: the API refuses loudly instead of silently falling back.</summary>
public class UnconfiguredProviderTests : IDisposable
{
    private readonly ApiFactory _f = new() { AiProvider = "" };

    [Fact]
    public async Task Search_fails_with_a_clear_not_configured_message()
    {
        var c = _f.Visitor();
        var id = (await (await c.PostAsync("api/searches", ApiFactory.Photo())).Content.ReadFromJsonAsync<CreatedSearchDto>())!.Id;
        var s = await ApiFactory.WaitForFinish(c, id);
        Assert.Equal(SearchStatus.Failed, s.Status);
        Assert.Contains("AI:Provider is not set", s.Error);
        var cfg = await c.GetFromJsonAsync<Dictionary<string, string>>("api/config");
        Assert.Equal("NotConfigured", cfg!["aiProvider"]);
    }

    public void Dispose() => _f.Dispose();
}
