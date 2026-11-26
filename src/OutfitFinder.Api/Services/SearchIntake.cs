using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Storage;
using static OutfitFinder.Api.Endpoints.SearchEndpoints;

namespace OutfitFinder.Api.Services;

/// <summary>
/// Upload path: limits → CAPTCHA → validate + re-encode (strips EXIF/GPS) → moderation FIRST → temp blob → Pending row.
/// A rejected image is never stored or forwarded; only its hash and the reason are recorded.
/// </summary>
public sealed class SearchIntake(
    OutfitDbContext db, IImageProcessor images, IImageModerator moderator, IBlobStore blobs,
    UsageGuard guard, ICaptchaVerifier captcha, ILogger<SearchIntake> log)
{
    public async Task<IResult> CreateAsync(HttpContext ctx, byte[] upload, SearchSource source, CancellationToken ct, bool bypassCache = false)
    {
        var caller = ctx.Caller();
        if (!caller.IsKnown) return Error(400, "Missing visitor id. Reload the page and try again.");

        var check = await guard.CheckAsync(caller, ct);
        if (!check.Allowed) return Error(check.Status, check.Message);

        if (guard.CaptchaRequired(caller) && source == SearchSource.Web)
        {
            var token = ctx.Request.Headers[Headers.Turnstile].ToString();
            if (!await captcha.VerifyAsync(token, ctx.Connection.RemoteIpAddress?.ToString(), ct))
                return Error(400, "Please complete the 'I am human' check and try again.");
        }

        ProcessedImage img;
        try { img = images.Normalise(upload); }
        catch (InvalidImageException e) { return Error(415, e.Message); }

        var moderation = await moderator.CheckAsync(img.Jpeg, ct);
        if (!moderation.Allowed)
        {
            db.ModerationEvents.Add(new ModerationEvent
            {
                Id = Guid.NewGuid(), ImageHash = img.Sha256, Reason = moderation.Reason ?? "rejected",
                VisitorId = caller.VisitorId, UserId = caller.UserId, IpHash = caller.IpHash, CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            log.LogWarning("Upload rejected by moderation: {Reason}", moderation.Reason);
            return Error(422, "This photo can't be searched because it may contain unsafe content.");
        }

        await caller.EnsureVisitorAsync(db, ct);
        var id = Guid.NewGuid();
        var pendingPath = BlobPaths.Pending(id);
        await blobs.SaveAsync(pendingPath, img.Jpeg, "image/jpeg", ct);
        db.Searches.Add(new OutfitSearch
        {
            Id = id, VisitorId = caller.VisitorId ?? Guid.Empty, UserId = caller.UserId, PendingBlobPath = pendingPath,
            // "Search again" must not be answered from the cache, so it gets a unique hash.
            ImageHash = bypassCache ? Infrastructure.Imaging.ImageProcessor.Sha256(System.Text.Encoding.UTF8.GetBytes(img.Sha256 + id)) : img.Sha256, IpHash = caller.IpHash, Status = SearchStatus.Pending,
            Stage = SearchStage.Queued, Source = source, RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return Results.Accepted($"/api/searches/{id}", new CreatedSearchDto(id));
    }
}
