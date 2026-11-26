using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using Testcontainers.MySql;

namespace OutfitFinder.Tests.Integration;

/// <summary>Real MySQL 8 in Docker: the database is created by migrations only, then a round trip.</summary>
public class MySqlTests
{
    [SkippableFact]
    public async Task Migrations_create_the_schema_on_mysql_8()
    {
        MySqlContainer container;
        try
        {
            container = new MySqlBuilder("mysql:8.4").WithCommand("--character-set-server=utf8mb4", "--collation-server=utf8mb4_0900_ai_ci").Build();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await container.StartAsync(cts.Token);
        }
        catch (Exception e) when (e is not Xunit.SkipException)
        {
            Skip.If(true, "Docker is not available: " + e.GetType().Name + ": " + e.Message);
            return;
        }

        await using (container)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "MySql",
                ["ConnectionStrings:OutfitDb"] = container.GetConnectionString(),
            }).Build();
            var services = new ServiceCollection().AddOutfitDatabase(config).BuildServiceProvider();
            await DatabaseSetup.MigrateAsync(services, config);

            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var id = Guid.NewGuid();
            db.Searches.Add(new OutfitSearch { Id = id, ImageHash = new string('a', 64), Status = SearchStatus.Pending, RowVersion = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var back = await scope.ServiceProvider.GetRequiredService<OutfitDbContext>().Searches.AsNoTracking().SingleAsync(s => s.Id == id);
            Assert.Equal(SearchStatus.Pending, back.Status);
            Assert.Equal(DateTimeKind.Utc, back.CreatedUtc.Kind);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.GetPendingMigrationsAsync());
        }
    }
}
