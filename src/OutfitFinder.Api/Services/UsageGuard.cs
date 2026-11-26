using Microsoft.EntityFrameworkCore;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Api.Services;

public sealed class LimitOptions
{
    public int SearchesPerVisitorPerDay { get; set; } = 5;
    public int SearchesPerUserPerDay { get; set; } = 25;
    public int SearchesPerIpPerDay { get; set; } = 60;
    public decimal DailyBudgetUsd { get; set; }
    public int ModerationRejectionsBeforeBlock { get; set; } = 3;
    public bool RequireCaptchaForAnonymousUpload { get; set; } = true;
}

public sealed record GuardResult(int Status, string Message)
{
    public static readonly GuardResult Ok = new(200, "");
    public bool Allowed => Status == 200;
}

/// <summary>Rate limits per visitor / user / IP, the global daily AI budget, and auto-block after repeated rejections.</summary>
public sealed class UsageGuard(OutfitDbContext db, IConfiguration config, ILogger<UsageGuard> log)
{
    private LimitOptions Limits => config.GetSection("Limits").Get<LimitOptions>() ?? new LimitOptions();

    public async Task<GuardResult> CheckAsync(Caller caller, CancellationToken ct)
    {
        var limits = Limits;
        var dayStart = DateTime.UtcNow.Date;
        var since = DateTime.UtcNow.AddHours(-24);

        var rejections = await db.ModerationEvents.CountAsync(m => m.CreatedUtc >= since &&
            ((caller.VisitorId != null && m.VisitorId == caller.VisitorId) ||
             (caller.UserId != null && m.UserId == caller.UserId) ||
             (caller.IpHash != null && m.IpHash == caller.IpHash)), ct);
        if (rejections >= limits.ModerationRejectionsBeforeBlock)
            return new(403, "Uploads from this device are paused for 24 hours after repeated unsafe photos.");

        if (limits.DailyBudgetUsd > 0)
        {
            // Summed in memory: SQLite (tests) cannot aggregate decimals, and there are few rows per day.
            var spent = (await db.Searches.Where(s => s.CreatedUtc >= dayStart && s.CostUsd > 0).Select(s => s.CostUsd).ToListAsync(ct)).Sum();
            if (spent >= limits.DailyBudgetUsd)
            {
                log.LogCritical("ALERT: daily AI budget reached ({Spent:0.00} of {Budget:0.00} USD). Searches are paused until midnight UTC.", spent, limits.DailyBudgetUsd);
                return new(503, "Searching is paused for today because we've reached our daily limit. Please try again tomorrow.");
            }
        }

        var today = db.Searches.Where(s => s.CreatedUtc >= dayStart && s.Status != SearchStatus.Rejected);
        if (caller.UserId is not null)
        {
            if (await today.CountAsync(s => s.UserId == caller.UserId, ct) >= limits.SearchesPerUserPerDay)
                return new(429, $"You've used today's {limits.SearchesPerUserPerDay} searches. Try again tomorrow.");
        }
        else if (await today.CountAsync(s => s.VisitorId == caller.VisitorId, ct) >= limits.SearchesPerVisitorPerDay)
        {
            return new(429, $"You've used today's {limits.SearchesPerVisitorPerDay} free searches. Create an account for more.");
        }
        if (caller.IpHash is not null && await today.CountAsync(s => s.IpHash == caller.IpHash, ct) >= limits.SearchesPerIpPerDay)
            return new(429, "Too many searches from this network today. Try again tomorrow.");

        return GuardResult.Ok;
    }

    public bool CaptchaRequired(Caller caller) => caller.UserId is null && Limits.RequireCaptchaForAnonymousUpload;
}
