using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutfitFinder.Api.Services;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Imaging;
using OutfitFinder.Infrastructure.Storage;

namespace OutfitFinder.Api.Endpoints;

public static class SearchEndpoints
{
    public static IResult Error(int status, string message) => Results.Json(new ApiError(message), statusCode: status);

    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api");

        g.MapPost("/searches", async (HttpContext ctx, [FromForm] IFormFile? photo, [FromForm] string? source, SearchIntake intake, CancellationToken ct) =>
        {
            if (photo is null || photo.Length == 0) return Error(400, "Please choose a photo.");
            if (photo.Length > ImageProcessor.MaxBytes) return Error(413, "Photos must be 10 MB or smaller.");
            using var ms = new MemoryStream();
            await photo.CopyToAsync(ms, ct);
            var src = Enum.TryParse<SearchSource>(source, true, out var s) && s != SearchSource.ExtensionUrl ? s : SearchSource.Web;
            return await intake.CreateAsync(ctx, ms.ToArray(), src, ct);
        }).DisableAntiforgery().WithRequestTimeout(TimeSpan.FromSeconds(60));

        g.MapPost("/searches/from-url", async (HttpContext ctx, SearchFromUrlRequest req, ISafeImageFetcher fetcher, SearchIntake intake, CancellationToken ct) =>
        {
            byte[] bytes;
            try { bytes = await fetcher.FetchAsync(req.Url, ct); }
            catch (ImageFetchException e) { return Error(422, e.Message); }
            return await intake.CreateAsync(ctx, bytes, SearchSource.ExtensionUrl, ct);
        });

        g.MapGet("/searches", async (HttpContext ctx, OutfitDbContext db, UrlSigner signer, int? take, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            if (!caller.IsKnown) return Results.Ok(Array.Empty<SearchSummaryDto>());
            var rows = await caller.Own(db.Searches).Where(s => s.Status != SearchStatus.Rejected)
                .OrderByDescending(s => s.CreatedUtc).Take(Math.Clamp(take ?? 60, 1, 200))
                .Select(s => new { s.Id, s.Status, Count = s.Items.Count, s.BlurredBlobPath, s.CreatedUtc,
                                   Items = s.Items.OrderBy(i => i.Number).Select(i => new { i.Name, i.Category }).ToList() }).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new SearchSummaryDto(r.Id, r.Status, r.Count, r.BlurredBlobPath is null ? null : signer.SearchImage(r.Id), r.CreatedUtc,
                SearchSummaryDto.PickTitle(r.Items.Select(i => (i.Name, i.Category))))));
        });

        g.MapGet("/searches/{id:guid}", async (Guid id, HttpContext ctx, OutfitDbContext db, UrlSigner signer, CancellationToken ct) =>
        {
            var s = await ctx.Caller().Own(db.Searches).AsNoTracking().Include(x => x.Items).ThenInclude(i => i.Matches).FirstOrDefaultAsync(x => x.Id == id, ct);
            return s is null ? Error(404, "We couldn't find that look. It may have been deleted.")
                : Results.Ok(s.ToDto(s.BlurredBlobPath is null ? null : signer.SearchImage(s.Id)));
        });

        g.MapDelete("/searches/{id:guid}", async (Guid id, HttpContext ctx, OutfitDbContext db, DataDeleter deleter, CancellationToken ct) =>
        {
            var exists = await ctx.Caller().Own(db.Searches).AnyAsync(x => x.Id == id, ct);
            if (!exists) return Error(404, "We couldn't find that look.");
            await deleter.DeleteSearchesAsync([id], ct);
            return Results.NoContent();
        });

        g.MapPost("/searches/{id:guid}/search-again", async (Guid id, HttpContext ctx, OutfitDbContext db, IBlobStore blobs, SearchIntake intake, CancellationToken ct) =>
        {
            var s = await ctx.Caller().Own(db.Searches).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s?.BlurredBlobPath is null) return Error(404, "We couldn't find that look's photo.");
            var bytes = await blobs.ReadAsync(s.BlurredBlobPath, ct);
            if (bytes is null) return Error(404, "That photo has been deleted.");
            // The stored copy is already blurred and re-encoded; it goes through moderation and limits again.
            return await intake.CreateAsync(ctx, bytes, s.Source, ct, bypassCache: true);
        });

        g.MapPut("/searches/{id:guid}/items", async (Guid id, AdjustItemsRequest req, HttpContext ctx, ItemAdjuster adjuster, CancellationToken ct) =>
        {
            if (req.Items.Count > 20) return Error(400, "Too many boxes.");
            var result = await adjuster.AdjustAsync(ctx.Caller(), id, req, ct);
            return result;
        }).WithRequestTimeout(TimeSpan.FromMinutes(5));

        g.MapPut("/matches/{id:guid}/feedback", async (Guid id, FeedbackRequest req, HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (match is null) return Error(404, "Not found.");
            var item = await db.Items.FirstAsync(i => i.Id == match.OutfitItemId, ct);
            if (!await caller.Own(db.Searches).AnyAsync(s => s.Id == item.OutfitSearchId, ct)) return Error(404, "Not found.");
            match.Feedback = req.Feedback;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Signed, short-lived link to the blurred photo (works in <img> without auth headers).
        g.MapGet("/images/{id:guid}", async (Guid id, long exp, string? sig, UrlSigner signer, OutfitDbContext db, IBlobStore blobs, CancellationToken ct) =>
        {
            if (!signer.Verify($"/api/images/{id:N}", exp, sig)) return Results.StatusCode(403);
            var path = await db.Searches.Where(s => s.Id == id).Select(s => s.BlurredBlobPath).FirstOrDefaultAsync(ct);
            var bytes = path is null ? null : await blobs.ReadAsync(path, ct);
            return bytes is null ? Results.NotFound() : Results.File(bytes, "image/jpeg");
        }).CacheOutput(p => p.Expire(TimeSpan.FromMinutes(30)).SetVaryByQuery("exp", "sig"));

        g.MapPost("/reports", async (ReportRequest req, HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason)) return Error(400, "Please choose a reason.");
            var caller = ctx.Caller();
            db.ProblemReports.Add(new ProblemReport
            {
                Id = Guid.NewGuid(), Kind = req.Reason == "takedown" ? ReportKind.Takedown : ReportKind.Problem,
                OutfitSearchId = req.SearchId, VisitorId = caller.VisitorId, UserId = caller.UserId,
                ContactEmail = Cut(req.ContactEmail, 256), ContentUrl = Cut(req.ContentUrl, 2048),
                Reason = Cut(req.Reason, 64)!, Details = Cut(req.Details, 4000) ?? "", CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
