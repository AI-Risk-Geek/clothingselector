using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OutfitFinder.Api.Services;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Patterns;
using OutfitFinder.Infrastructure.Storage;
using static OutfitFinder.Api.Endpoints.SearchEndpoints;

namespace OutfitFinder.Api.Endpoints;

public static class PatternEndpoints
{
    public static void MapPatternEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api");

        g.MapPost("/items/{itemId:guid}/pattern", async (Guid itemId, PatternRequest req, HttpContext ctx, OutfitDbContext db, PatternEngine engine, UrlSigner signer, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
            if (item is null || !await caller.Own(db.Searches).AnyAsync(s => s.Id == item.OutfitSearchId, ct)) return Error(404, "We couldn't find that piece.");

            var makeIt = item.MakeItJson is null ? new MakeItInfo() : JsonSerializer.Deserialize<MakeItInfo>(item.MakeItJson, AnalysisJson.Options) ?? new MakeItInfo();
            if (!item.Makeable || !engine.Supports(makeIt.GarmentType)) return Error(422, "No pattern for this piece yet.");

            Measurements m; string label;
            try
            {
                if (req.MeasurementProfileId is { } pid)
                {
                    var profile = await OwnProfiles(db, caller).FirstOrDefaultAsync(p => p.Id == pid, ct);
                    if (profile is null) return Error(404, "Measurements not found.");
                    m = Measurements.FromDictionary(JsonSerializer.Deserialize<Dictionary<string, double>>(profile.MeasurementsJson)!);
                    label = profile.Name;
                }
                else if (req.Measurements is { Count: > 0 } custom) { m = Measurements.FromDictionary(custom); label = "Custom"; }
                else { label = (req.Size ?? "M").ToUpperInvariant(); m = SizeChart.Get(label); }
            }
            catch (ArgumentException e) { return Error(400, e.Message); }

            var pattern = engine.Draft(makeIt.GarmentType, m, label);
            var materials = MaterialsCalculator.Compute(pattern, makeIt.FabricSuggestion);
            var job = new PatternJob
            {
                Id = Guid.NewGuid(), OutfitItemId = item.Id, Block = pattern.Block, Status = PatternJobStatus.Completed,
                OptionsJson = JsonSerializer.Serialize(new { size = label, makeIt.Fit, makeIt.Length }),
                MeasurementsSnapshot = JsonSerializer.Serialize(m.ToDictionary()), CreatedUtc = DateTime.UtcNow,
                Materials = materials.Select(x => new MaterialLine { Id = Guid.NewGuid(), Kind = x.Kind, Description = x.Description, Quantity = x.Quantity, Unit = x.Unit }).ToList(),
            };
            db.PatternJobs.Add(job);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new PatternDto(job.Id, pattern.Block, pattern.GarmentName, label,
                pattern.Pieces.Select((p, i) => new PatternPieceDto(p.Name, p.OnFold ? 1 : p.Cut, p.OnFold, PatternRenderer.PreviewSvg(p, i))).ToList(),
                job.Materials.Select(x => x.ToDto()).ToList(), pattern.Steps, pattern.Difficulty,
                signer.PatternPdf(job.Id, "letter"), signer.PatternPdf(job.Id, "a4"), signer.PatternPdf(job.Id, "copyshop")));
        });

        // PDFs are generated on first download and kept with the job (deleted with the search).
        g.MapGet("/patterns/{jobId:guid}/pdf/{kind}", async (Guid jobId, string kind, long exp, string? sig, UrlSigner signer, OutfitDbContext db,
            PatternEngine engine, IBlobStore blobs, CancellationToken ct) =>
        {
            if (kind is not ("letter" or "a4" or "copyshop")) return Results.NotFound();
            if (!signer.Verify($"/api/patterns/{jobId:N}/pdf/{kind}", exp, sig)) return Results.StatusCode(403);
            var job = await db.PatternJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job is null) return Results.NotFound();

            var path = BlobPaths.PatternPdf(job.Id, kind);
            var pdf = await blobs.ReadAsync(path, ct);
            if (pdf is null)
            {
                var size = JsonDocument.Parse(job.OptionsJson).RootElement.TryGetProperty("size", out var s) ? s.GetString() ?? "M" : "M";
                var m = Measurements.FromDictionary(JsonSerializer.Deserialize<Dictionary<string, double>>(job.MeasurementsSnapshot)!);
                var pattern = engine.Draft(job.Block, m, size);
                pdf = kind == "copyshop" ? PatternRenderer.CopyShopPdf(pattern) : PatternRenderer.TiledPdf(pattern, kind == "a4" ? Paper.A4 : Paper.Letter);
                await blobs.SaveAsync(path, pdf, "application/pdf", ct);
                if (kind == "letter") job.PdfBlobPath = path;
                if (kind == "copyshop") job.A0PdfBlobPath = path;
                await db.SaveChangesAsync(ct);
            }
            return Results.File(pdf, "application/pdf", $"{Core.Brand.Name.Replace(' ', '-')}-{job.Block}-{kind}.pdf");
        });

        g.MapPost("/materials/{id:guid}/find", async (Guid id, HttpContext ctx, OutfitDbContext db, IAiSettingsProvider settingsProvider,
            IAnalyzerFactory analyzers, ILinkChecker links, IOutboundLinkSafety safety, UsageGuard guard, CancellationToken ct) =>
        {
            var caller = ctx.Caller();
            var line = await db.MaterialLines.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (line is null) return Error(404, "Not found.");
            var job = await db.PatternJobs.FirstAsync(j => j.Id == line.PatternJobId, ct);
            var item = await db.Items.FirstAsync(i => i.Id == job.OutfitItemId, ct);
            if (!await caller.Own(db.Searches).AnyAsync(s => s.Id == item.OutfitSearchId, ct)) return Error(404, "Not found.");
            var check = await guard.CheckAsync(caller, ct);
            if (!check.Allowed) return Error(check.Status, check.Message);

            var settings = await settingsProvider.GetAsync(ct);
            var result = await analyzers.For(settings.Provider).FindProductsAsync(line.Kind.ToString(), $"{line.Description}, {line.Quantity} {line.Unit}", null, settings, ct);
            var urls = result.Value.Matches.Select(m => m.Url).ToList();
            var matches = SearchPipeline.ToMatches(Guid.Empty, result.Value.Matches, await links.FindLiveAsync(urls, ct), await safety.FindUnsafeAsync(urls, ct));
            if (matches.FirstOrDefault() is { } top) { line.Url = top.Url; line.Price = top.Price; }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new FindMaterialResult(matches.Select(m => m.ToDto()).ToList()));
        }).WithRequestTimeout(TimeSpan.FromMinutes(3));
    }

    public static IQueryable<MeasurementProfile> OwnProfiles(OutfitDbContext db, Caller caller) =>
        caller.UserId is not null
            ? db.MeasurementProfiles.Where(p => p.UserId == caller.UserId)
            : db.MeasurementProfiles.Where(p => caller.VisitorId != null && p.VisitorId == caller.VisitorId && p.UserId == null);
}
