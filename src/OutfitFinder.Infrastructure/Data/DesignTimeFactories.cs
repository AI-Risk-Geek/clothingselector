using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace OutfitFinder.Infrastructure.Data;

/// <summary>Used only by 'dotnet ef' to create migrations. Migrations are always generated for MySQL.</summary>
internal static class DesignTimeConfig
{
    public static IConfiguration Build() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            // Design-time only: no server is contacted when generating migrations.
            ["ConnectionStrings:OutfitDb"] = Environment.GetEnvironmentVariable("OUTFITDB_DESIGN") ?? "server=localhost;port=3306;database=outfitfinder;user=design;password=design",
        }).Build();
}

internal sealed class OutfitDbDesignFactory : IDesignTimeDbContextFactory<OutfitDbContext>
{
    public OutfitDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<OutfitDbContext>();
        o.UseConfiguredProvider(DesignTimeConfig.Build(), "__EFMigrationsHistory");
        return new OutfitDbContext(o.Options);
    }
}

internal sealed class IdentityDbDesignFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<AppIdentityDbContext>();
        o.UseConfiguredProvider(DesignTimeConfig.Build(), "__EFMigrationsHistory_Identity");
        return new AppIdentityDbContext(o.Options);
    }
}
