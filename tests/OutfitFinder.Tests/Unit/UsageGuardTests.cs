using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OutfitFinder.Api.Services;
using OutfitFinder.Core.Entities;

namespace OutfitFinder.Tests.Unit;

public class UsageGuardTests
{
    private static UsageGuard Guard(SqliteDb s, decimal budget = 0) => new(s.Db,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Limits:SearchesPerUserPerDay"] = "25",
            ["Limits:SearchesPerIpPerDay"] = "60",
            ["Limits:DailyBudgetUsd"] = budget.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build(),
        NullLogger<UsageGuard>.Instance);

    /// <summary>Adds today's searches for one account, all from the same device and network.</summary>
    private static async Task AddSearches(SqliteDb s, string userId, int count, decimal costEach = 0)
    {
        var visitor = Guid.NewGuid();
        s.Db.Visitors.Add(new Visitor { Id = visitor, CreatedUtc = DateTime.UtcNow });
        for (var i = 0; i < count; i++)
            s.Db.Searches.Add(new OutfitSearch
            {
                Id = Guid.NewGuid(), VisitorId = visitor, UserId = userId, IpHash = "same-network", ImageHash = Guid.NewGuid().ToString("N"),
                Status = SearchStatus.Completed, Stage = SearchStage.Done, RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow, CostUsd = costEach,
            });
        await s.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_normal_account_stops_at_the_daily_limit_but_an_admin_does_not()
    {
        using var s = new SqliteDb();
        await AddSearches(s, "user-1", 25);
        await AddSearches(s, "admin-1", 80); // past both the account (25) and network (60) limits

        var user = await Guard(s).CheckAsync(new Caller("user-1", "user", null, "same-network", IsAdmin: false), default);
        Assert.Equal(429, user.Status);

        var admin = await Guard(s).CheckAsync(new Caller("admin-1", "admin", null, "same-network", IsAdmin: true), default);
        Assert.True(admin.Allowed);
    }

    [Fact]
    public async Task The_daily_budget_still_applies_to_admins()
    {
        using var s = new SqliteDb();
        await AddSearches(s, "admin-1", 3, costEach: 10m); // 30 USD spent today

        var admin = await Guard(s, budget: 25m).CheckAsync(new Caller("admin-1", "admin", null, "same-network", IsAdmin: true), default);
        Assert.Equal(503, admin.Status);
    }
}
