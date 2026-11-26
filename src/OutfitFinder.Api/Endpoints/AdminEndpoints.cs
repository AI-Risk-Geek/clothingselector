using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OutfitFinder.Api.Services;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Ai;
using OutfitFinder.Infrastructure.Data;
using static OutfitFinder.Api.Endpoints.SearchEndpoints;

namespace OutfitFinder.Api.Endpoints;

public static class AdminEndpoints
{
    public const string AdminPolicy = "Admin";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization(AdminPolicy);

        g.MapGet("/ai", async (OutfitDbContext db, IConfiguration config, CancellationToken ct) => Results.Ok(await CurrentAsync(db, config, ct)));

        g.MapPut("/ai", async (AiSettingsUpdate req, HttpContext ctx, OutfitDbContext db, IDataProtectionProvider dp, IConfiguration config,
            IHostEnvironment env, CancellationToken ct) =>
        {
            if (Validate(req, env) is { } problem) return Error(400, problem);
            var caller = ctx.Caller();
            var previous = await db.AiProviderSettings.OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(ct);
            var protector = dp.CreateProtector(AiSettingsProvider.ProtectorPurpose);
            var row = new AiProviderSetting
            {
                Id = Guid.NewGuid(), Provider = req.Provider, Model = req.Model.Trim(), Deployment = Blank(req.Deployment), Endpoint = Blank(req.Endpoint),
                WebSearchEnabled = req.WebSearchEnabled, UpdatedBy = caller.UserName ?? caller.UserId ?? "admin", UpdatedUtc = DateTime.UtcNow,
            };
            if (!string.IsNullOrWhiteSpace(req.ApiKey))
            {
                row.EncryptedKey = protector.Protect(req.ApiKey.Trim());
                row.KeyLast4 = req.ApiKey.Trim()[^Math.Min(4, req.ApiKey.Trim().Length)..];
            }
            else if (previous is not null)
            {
                row.EncryptedKey = previous.EncryptedKey; row.KeyLast4 = previous.KeyLast4;
            }
            db.AiProviderSettings.Add(row);
            // Audit: never the key itself.
            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(), Actor = row.UpdatedBy, Action = "ai-settings.update", CreatedUtc = DateTime.UtcNow,
                Details = $"provider {previous?.Provider.ToString() ?? "(config)"} -> {row.Provider}; model {row.Model}; deployment {row.Deployment}; endpoint {row.Endpoint}; webSearch {row.WebSearchEnabled}; keyChanged {!string.IsNullOrWhiteSpace(req.ApiKey)}",
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await CurrentAsync(db, config, ct));
        });

        // Runs the sample photo through the given (unsaved) settings and reports what came back.
        g.MapPost("/ai/test", async (AiSettingsUpdate req, OutfitDbContext db, IDataProtectionProvider dp, IAnalyzerFactory analyzers, IImageProcessor images,
            ILinkChecker links, IConfiguration config, IWebHostEnvironment env, CancellationToken ct) =>
        {
            if (Validate(req, env) is { } problem) return Error(400, problem);
            var key = req.ApiKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                var saved = await db.AiProviderSettings.OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(ct);
                key = saved is { EncryptedKey.Length: > 0 } ? dp.CreateProtector(AiSettingsProvider.ProtectorPurpose).Unprotect(saved.EncryptedKey) : config["AI:ApiKey"] ?? "";
            }
            var settings = new AiRuntimeSettings(req.Provider, req.Model, Blank(req.Deployment), Blank(req.Endpoint), key ?? "", req.WebSearchEnabled, config.GetValue("AI:TimeoutSeconds", 120));
            var sw = Stopwatch.StartNew();
            try
            {
                settings.EnsureConfigured();
                var photo = images.Normalise(await File.ReadAllBytesAsync(Path.Combine(env.ContentRootPath, "Samples", "sample-outfit.jpg"), ct));
                var result = await analyzers.For(req.Provider).AnalyzeAsync(photo.Jpeg, settings, ct);
                var urls = result.Value.Items.SelectMany(i => i.Matches).Select(m => m.Url).ToList();
                var live = await links.FindLiveAsync(urls, ct);
                return Results.Ok(new AiTestResult(true,
                    $"Connected. Sample result: {result.Value.Items.Count} pieces, {live.Count} working links of {urls.Count}, {sw.Elapsed.TotalSeconds:0} seconds.",
                    result.Value.Items.Count, live.Count, urls.Count, sw.Elapsed.TotalSeconds, result.CostUsd));
            }
            catch (Exception e) when (e is AiNotConfiguredException or AiResponseException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return Results.Ok(new AiTestResult(false, e.Message, 0, 0, 0, sw.Elapsed.TotalSeconds, 0));
            }
        }).WithRequestTimeout(TimeSpan.FromMinutes(5));

        g.MapGet("/audit", async (OutfitDbContext db, CancellationToken ct) =>
            Results.Ok(await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.CreatedUtc).Take(50)
                .Select(a => new AuditEntryDto(a.Actor, a.Action, a.Details, a.CreatedUtc)).ToListAsync(ct)));
    }

    private static string? Validate(AiSettingsUpdate req, IHostEnvironment env)
    {
        if (req.Provider == AiProviderKind.Demo && !env.IsDevelopment()) return "The Demo provider is only available in development.";
        if (req.Provider != AiProviderKind.Demo && string.IsNullOrWhiteSpace(req.Model) && string.IsNullOrWhiteSpace(req.Deployment)) return "Enter a model name.";
        if (req.Provider == AiProviderKind.AzureAiFoundry && !Uri.TryCreate(req.Endpoint, UriKind.Absolute, out _)) return "Enter the Azure endpoint URL.";
        if (!string.IsNullOrWhiteSpace(req.Endpoint) && !(Uri.TryCreate(req.Endpoint, UriKind.Absolute, out var u) && u.Scheme == "https")) return "The endpoint must be an https URL.";
        return null;
    }

    private static async Task<AiSettingsDto> CurrentAsync(OutfitDbContext db, IConfiguration config, CancellationToken ct)
    {
        var row = await db.AiProviderSettings.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(ct);
        if (row is not null)
            return new AiSettingsDto(row.Provider, row.Model, row.Deployment, row.Endpoint, row.KeyLast4, row.WebSearchEnabled, row.UpdatedBy, row.UpdatedUtc, "admin");
        var o = config.GetSection("AI").Get<AiOptions>() ?? new AiOptions();
        _ = Enum.TryParse<AiProviderKind>(o.Provider, true, out var kind);
        var last4 = string.IsNullOrEmpty(o.ApiKey) ? null : o.ApiKey[^Math.Min(4, o.ApiKey.Length)..];
        return new AiSettingsDto(kind, o.Model, o.Deployment, o.Endpoint, last4, o.WebSearchEnabled, null, null, "configuration");
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
