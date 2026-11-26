using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OutfitFinder.Api.Services;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Patterns;
using OutfitFinder.UI.Legal;
using static OutfitFinder.Api.Endpoints.SearchEndpoints;

namespace OutfitFinder.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var anyone = app.MapGroup("/api/me");
        var signedIn = app.MapGroup("/api/me").RequireAuthorization();

        // Move this device's anonymous looks (and measurements) into the signed-in account.
        signedIn.MapPost("/merge-visitor/{visitorId:guid}", async (Guid visitorId, HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, ct);
            if (visitor is null) return Results.NoContent();
            if (visitor.UserId is not null && visitor.UserId != caller.UserId) return Error(409, "This device is linked to another account.");
            visitor.UserId = caller.UserId;
            foreach (var s in await db.Searches.Where(s => s.VisitorId == visitorId && s.UserId == null).ToListAsync(ct)) s.UserId = caller.UserId;
            foreach (var p in await db.MeasurementProfiles.Where(p => p.VisitorId == visitorId && p.UserId == null).ToListAsync(ct)) p.UserId = caller.UserId;
            foreach (var c in await db.Consents.Where(c => c.VisitorId == visitorId && c.UserId == null).ToListAsync(ct)) c.UserId = caller.UserId;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        anyone.MapDelete("/history", async (HttpContext ctx, OutfitDbContext db, DataDeleter deleter, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            if (!caller.IsKnown) return Results.NoContent();
            var ids = await caller.Own(db.Searches).Select(s => s.Id).ToListAsync(ct);
            await deleter.DeleteSearchesAsync(ids, ct);
            return Results.NoContent();
        });

        signedIn.MapDelete("", async (HttpContext ctx, OutfitDbContext db, DataDeleter deleter, UserManager<ApplicationUser> users, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            var ids = await db.Searches.Where(s => s.UserId == caller.UserId).Select(s => s.Id).ToListAsync(ct);
            await deleter.DeleteSearchesAsync(ids, ct);
            db.MeasurementProfiles.RemoveRange(db.MeasurementProfiles.Where(p => p.UserId == caller.UserId));
            db.Consents.RemoveRange(db.Consents.Where(c => c.UserId == caller.UserId));
            db.Visitors.RemoveRange(db.Visitors.Where(v => v.UserId == caller.UserId));
            foreach (var r in await db.ProblemReports.Where(r => r.UserId == caller.UserId).ToListAsync(ct)) r.UserId = null;
            await db.SaveChangesAsync(ct);
            var user = await users.FindByIdAsync(caller.UserId!);
            if (user is not null)
            {
                var res = await users.DeleteAsync(user);
                if (!res.Succeeded) return Error(500, "Your data was deleted but the account could not be removed. Please contact us.");
            }
            return Results.NoContent();
        });

        // Download my data: JSON + blurred images, as a zip.
        anyone.MapGet("/export", async (HttpContext ctx, OutfitDbContext db, IBlobStore blobs, UserManager<ApplicationUser> users, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            if (!caller.IsKnown) return Error(400, "Nothing to export.");
            var searches = await caller.Own(db.Searches).AsNoTracking().Include(s => s.Items).ThenInclude(i => i.Matches).ToListAsync(ct);
            var itemIds = searches.SelectMany(s => s.Items).Select(i => i.Id).ToList();
            var user = caller.UserId is null ? null : await users.FindByIdAsync(caller.UserId);
            var data = new
            {
                exportedUtc = DateTime.UtcNow,
                account = user is null ? null : new { user.Id, user.UserName, user.Email, user.CreatedUtc },
                visitorId = caller.VisitorId,
                searches = searches.Select(s => new
                {
                    s.Id, s.Status, s.Source, s.Provider, s.Model, s.CreatedUtc, s.CompletedUtc, image = s.BlurredBlobPath is null ? null : $"images/{s.Id:N}.jpg",
                    items = s.Items.OrderBy(i => i.Number).Select(i => new
                    {
                        i.Number, i.Category, i.Name, i.Description, i.BrandHints, box = new { i.BoxX, i.BoxY, i.BoxW, i.BoxH },
                        matches = i.Matches.OrderBy(m => m.Rank).Select(m => new { m.Store, m.Title, m.Price, m.Currency, m.Url, m.MatchType, m.Feedback }),
                    }),
                }),
                patterns = await db.PatternJobs.AsNoTracking().Where(j => itemIds.Contains(j.OutfitItemId)).Include(j => j.Materials)
                    .Select(j => new { j.Id, j.Block, j.OptionsJson, j.MeasurementsSnapshot, j.CreatedUtc, materials = j.Materials.Select(m => new { m.Description, m.Quantity, m.Unit, m.Url }) }).ToListAsync(ct),
                measurements = await PatternEndpoints.OwnProfiles(db, caller).AsNoTracking().Select(p => new { p.Name, p.MeasurementsJson, p.UpdatedUtc }).ToListAsync(ct),
                consents = await db.Consents.AsNoTracking().Where(c => (caller.UserId != null && c.UserId == caller.UserId) || (caller.VisitorId != null && c.VisitorId == caller.VisitorId))
                    .Select(c => new { c.Document, c.Version, c.AcceptedUtc }).ToListAsync(ct),
            };

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var json = zip.CreateEntry("data.json");
                await using (var w = json.Open()) await JsonSerializer.SerializeAsync(w, data, OutfitFinder.UI.Services.OutfitApiClient.Json, ct);
                foreach (var s in searches.Where(s => s.BlurredBlobPath is not null))
                {
                    var bytes = await blobs.ReadAsync(s.BlurredBlobPath!, ct);
                    if (bytes is null) continue;
                    await using var e = zip.CreateEntry($"images/{s.Id:N}.jpg", CompressionLevel.NoCompression).Open();
                    await e.WriteAsync(bytes, ct);
                }
            }
            return Results.File(ms.ToArray(), "application/zip", $"{Core.Brand.Name.Replace(' ', '-')}-my-data.zip");
        });

        anyone.MapGet("/measurements", async (HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            if (!caller.IsKnown) return Results.Ok(Array.Empty<MeasurementProfileDto>());
            var rows = await PatternEndpoints.OwnProfiles(db, caller).AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
            return Results.Ok(rows.Select(p => new MeasurementProfileDto(p.Id, p.Name, JsonSerializer.Deserialize<Dictionary<string, double>>(p.MeasurementsJson)!)));
        });

        anyone.MapPut("/measurements", async (MeasurementProfileDto dto, HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            if (!caller.IsKnown) return Error(400, "Accept the cookie or sign in to save measurements.");
            var clean = dto.Measurements.Where(kv => Measurements.Keys.Contains(kv.Key) && kv.Value > 0).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 1));
            try { Measurements.FromDictionary(clean); } catch (ArgumentException e) { return Error(400, e.Message); }
            await caller.EnsureVisitorAsync(db, ct);
            var profile = dto.Id is { } id ? await PatternEndpoints.OwnProfiles(db, caller).FirstOrDefaultAsync(p => p.Id == id, ct) : null;
            if (profile is null)
            {
                profile = new MeasurementProfile { Id = Guid.NewGuid(), UserId = caller.UserId, VisitorId = caller.UserId is null ? caller.VisitorId : null };
                db.MeasurementProfiles.Add(profile);
            }
            profile.Name = string.IsNullOrWhiteSpace(dto.Name) ? "My measurements" : dto.Name.Trim()[..Math.Min(64, dto.Name.Trim().Length)];
            profile.MeasurementsJson = JsonSerializer.Serialize(clean);
            profile.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new MeasurementProfileDto(profile.Id, profile.Name, clean));
        });

        anyone.MapDelete("/measurements/{id:guid}", async (Guid id, HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            var profile = await PatternEndpoints.OwnProfiles(db, ctx.Caller()).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (profile is not null) { db.MeasurementProfiles.Remove(profile); await db.SaveChangesAsync(ct); }
            return Results.NoContent();
        });

        // Re-consent: which current Terms / Privacy versions has this user not accepted yet?
        signedIn.MapGet("/consents", async (HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            var accepted = await db.Consents.Where(c => c.UserId == caller.UserId).Select(c => new { c.Document, c.Version }).ToListAsync(ct);
            var outstanding = LegalDocuments.Current.Where(d => !accepted.Any(a => a.Document == d.Document && a.Version == d.Version)).ToList();
            return Results.Ok(new ConsentStatusDto(outstanding));
        });

        signedIn.MapPost("/consents", async (ConsentRequest req, HttpContext ctx, OutfitDbContext db, CancellationToken ct) =>
        {
            var current = LegalDocuments.Current.FirstOrDefault(d => d.Document == req.Document && d.Version == req.Version);
            if (current is null) return Error(400, "Unknown document version.");
            db.Consents.Add(new ConsentRecord { Id = Guid.NewGuid(), UserId = ctx.Caller().UserId, Document = req.Document, Version = req.Version, AcceptedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
