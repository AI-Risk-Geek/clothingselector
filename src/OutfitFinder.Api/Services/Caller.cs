using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OutfitFinder.Core.Contracts;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Api.Services;

/// <summary>Who is calling: a signed-in user, an anonymous visitor, or both (just after sign-in).</summary>
public sealed record Caller(string? UserId, string? UserName, Guid? VisitorId, string? IpHash, bool IsAdmin)
{
    public bool IsKnown => UserId is not null || VisitorId is not null;

    public static Caller From(HttpContext ctx, IConfiguration config)
    {
        var user = ctx.User.Identity?.IsAuthenticated == true ? ctx.User : null;
        var userId = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = user?.FindFirstValue("name") ?? user?.Identity?.Name;
        Guid? visitor = Guid.TryParse(ctx.Request.Headers[Headers.VisitorId], out var v) && v != Guid.Empty ? v : null;
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        var salt = config["Limits:IpHashSalt"] ?? "outfit-finder";
        string? ipHash = ip is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + ip)))[..32];
        var isAdmin = user?.IsInRole(Roles.Admin) == true || user?.HasClaim("role", Roles.Admin) == true;
        return new Caller(userId, name, visitor, ipHash, isAdmin);
    }

    /// <summary>Searches this caller may see: their account's, plus this device's anonymous ones.</summary>
    public IQueryable<OutfitSearch> Own(IQueryable<OutfitSearch> q) =>
        UserId is not null
            ? q.Where(s => s.UserId == UserId || (VisitorId != null && s.VisitorId == VisitorId && s.UserId == null))
            : q.Where(s => VisitorId != null && s.VisitorId == VisitorId && s.UserId == null);

    public async Task EnsureVisitorAsync(OutfitDbContext db, CancellationToken ct)
    {
        if (VisitorId is not { } id) return;
        if (!await db.Visitors.AnyAsync(v => v.Id == id, ct))
        {
            db.Visitors.Add(new Visitor { Id = id, CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
    }
}

public static class CallerExtensions
{
    public static Caller Caller(this HttpContext ctx) =>
        OutfitFinder.Api.Services.Caller.From(ctx, ctx.RequestServices.GetRequiredService<IConfiguration>());
}
