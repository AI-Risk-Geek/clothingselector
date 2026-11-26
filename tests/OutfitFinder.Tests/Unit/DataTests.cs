using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Tests.Unit;

public sealed class SqliteDb : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    public OutfitDbContext Db { get; }

    public SqliteDb()
    {
        _conn.Open();
        Db = new OutfitDbContext(new DbContextOptionsBuilder<OutfitDbContext>().UseSqlite(_conn).Options);
        Db.Database.EnsureCreated();
    }

    public OutfitDbContext NewContext() => new(new DbContextOptionsBuilder<OutfitDbContext>().UseSqlite(_conn).Options);

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}

public class DataTests
{
    [Fact]
    public async Task Search_with_items_and_matches_round_trips()
    {
        using var s = new SqliteDb();
        var id = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        s.Db.Searches.Add(new OutfitSearch
        {
            Id = id, VisitorId = Guid.NewGuid(), ImageHash = "abc", Status = SearchStatus.Completed, Stage = SearchStage.Done,
            RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow,
            Items = [new OutfitItem { Id = itemId, OutfitSearchId = id, Number = 1, Category = "Top", Name = "Shirt",
                Matches = [new ItemMatch { Id = Guid.NewGuid(), OutfitItemId = itemId, Store = "S", Title = "T", Url = "https://x.test/p", Price = 12.5m, Currency = "USD", MatchType = MatchKind.Exact }] }],
        });
        await s.Db.SaveChangesAsync();

        await using var fresh = s.NewContext();
        var loaded = await fresh.Searches.Include(x => x.Items).ThenInclude(i => i.Matches).SingleAsync(x => x.Id == id);
        Assert.Equal(SearchStatus.Completed, loaded.Status);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedUtc.Kind);
        Assert.Equal(12.5m, loaded.Items.Single().Matches.Single().Price);
        Assert.Equal(MatchKind.Exact, loaded.Items.Single().Matches.Single().MatchType);
    }

    [Fact]
    public void Model_follows_plan_column_conventions()
    {
        using var s = new SqliteDb();
        var entity = s.Db.Model.FindEntityType(typeof(OutfitSearch))!;
        Assert.Equal("char(36)", entity.FindProperty(nameof(OutfitSearch.Id))!.GetColumnType());
        Assert.Equal("datetime(6)", entity.FindProperty(nameof(OutfitSearch.CreatedUtc))!.GetColumnType());
        // Enums stored as strings.
        var status = entity.FindProperty(nameof(OutfitSearch.Status))!;
        Assert.Equal(typeof(string), status.GetTypeMapping().Converter?.ProviderClrType ?? status.GetProviderClrType());
        Assert.True(entity.FindProperty(nameof(OutfitSearch.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Migrations_match_the_model_for_both_contexts()
    {
        // Schema is created by migrations only: the snapshot must include every model change.
        using var outfit = new OutfitDbDesignFactoryAccessor().Outfit();
        Assert.False(outfit.Database.HasPendingModelChanges(), "OutfitDbContext has model changes without a migration. Run dotnet ef migrations add.");
        using var identity = new OutfitDbDesignFactoryAccessor().Identity();
        Assert.False(identity.Database.HasPendingModelChanges(), "AppIdentityDbContext has model changes without a migration.");
        Assert.NotEmpty(outfit.Database.GetMigrations());
    }

    [Fact]
    public async Task Concurrency_token_stops_two_workers_claiming_the_same_row()
    {
        using var s = new SqliteDb();
        var id = Guid.NewGuid();
        s.Db.Searches.Add(new OutfitSearch { Id = id, ImageHash = "h", Status = SearchStatus.Pending, RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow });
        await s.Db.SaveChangesAsync();

        await using var a = s.NewContext();
        await using var b = s.NewContext();
        var rowA = await a.Searches.SingleAsync(x => x.Id == id);
        var rowB = await b.Searches.SingleAsync(x => x.Id == id);
        rowA.Status = SearchStatus.Running; rowA.RowVersion = Guid.NewGuid();
        rowB.Status = SearchStatus.Running; rowB.RowVersion = Guid.NewGuid();
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }
}

/// <summary>Builds the MySQL-configured contexts used by 'dotnet ef' (no server needed to compare the model).</summary>
internal sealed class OutfitDbDesignFactoryAccessor
{
    private static DbContextOptionsBuilder<T> Options<T>(string history) where T : DbContext
    {
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["ConnectionStrings:OutfitDb"] = "server=localhost;database=x;user=x;password=x",
        }).Build();
        var o = new DbContextOptionsBuilder<T>();
        o.UseConfiguredProvider(cfg, history);
        return o;
    }

    public OutfitDbContext Outfit() => new(Options<OutfitDbContext>("__EFMigrationsHistory").Options);
    public AppIdentityDbContext Identity() => new(Options<AppIdentityDbContext>("__EFMigrationsHistory_Identity").Options);
}
