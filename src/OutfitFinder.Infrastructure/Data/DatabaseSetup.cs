using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OutfitFinder.Infrastructure.Data;

public static class DatabaseSetup
{
    public const string ConnectionName = "OutfitDb";

    /// <summary>
    /// The one place the database provider is chosen (<c>Database:Provider</c>).
    /// MySql for every real environment; Sqlite only for unit tests and quick local UI runs.
    /// </summary>
    public static void UseConfiguredProvider(this DbContextOptionsBuilder options, IConfiguration config, string migrationsHistoryTable, string? migrationsAssembly = null)
    {
        var provider = config["Database:Provider"] ?? "MySql";
        var cs = config.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not set. Use 'dotnet user-secrets set ConnectionStrings:{ConnectionName} ...'.");

        switch (provider)
        {
            case "MySql":
                options.UseMySQL(cs, o =>
                {
                    o.MigrationsAssembly(migrationsAssembly ?? typeof(DatabaseSetup).Assembly.FullName);
                    o.MigrationsHistoryTable(migrationsHistoryTable);
                    o.EnableRetryOnFailure(3);
                });
                break;
            case "Sqlite":
                options.UseSqlite(WithAbsolutePath(cs, config), o => o.MigrationsHistoryTable(migrationsHistoryTable));
                break;
            default:
                throw new InvalidOperationException($"Unknown Database:Provider '{provider}'. Use MySql or Sqlite.");
        }
    }

    /// <summary>
    /// A relative SQLite file path is taken from the project folder (the host's content root), not the process's
    /// current folder: IIS Express starts in its own folder, so "../../.local/x.db" would otherwise point elsewhere.
    /// </summary>
    public static string WithAbsolutePath(string connectionString, IConfiguration config)
    {
        var b = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        if (b.DataSource is { Length: > 0 } file && file != ":memory:" && !Path.IsPathRooted(file))
            b.DataSource = Path.GetFullPath(Path.Combine(ContentRoot(config), file));
        // SQLite creates the file but not its folder; a fresh clone has no .local folder yet.
        if (b.DataSource is { Length: > 0 } full && full != ":memory:" && Path.GetDirectoryName(full) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        return b.ToString();
    }

    /// <summary>The project folder when running under a host ("contentRoot" is set by ASP.NET Core); otherwise the current folder.</summary>
    public static string ContentRoot(IConfiguration config) => config["contentRoot"] is { Length: > 0 } r ? r : Directory.GetCurrentDirectory();

    public static IServiceCollection AddOutfitDatabase(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<OutfitDbContext>(o => o.UseConfiguredProvider(config, "__EFMigrationsHistory"));
        services.AddDbContext<AppIdentityDbContext>(o => o.UseConfiguredProvider(config, "__EFMigrationsHistory_Identity"));
        return services;
    }

    /// <summary>Applies migrations (MySql) or creates the schema (Sqlite, which has no migrations).</summary>
    public static async Task MigrateAsync(IServiceProvider sp, IConfiguration config, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var outfit = scope.ServiceProvider.GetRequiredService<OutfitDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        if ((config["Database:Provider"] ?? "MySql") == "Sqlite")
        {
            // The search worker, web requests and the sign-in server all write to one SQLite file. In SQLite's default
            // journal mode a second writer can fail at once with "database is locked"; WAL mode makes writers wait
            // their turn instead. This is the one deliberate raw SQL statement: a SQLite file-format setting for the
            // local demo and tests only (it persists in the file), not data access, and it never runs against MySQL.
            await outfit.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
            // Several contexts (and apps) share one SQLite file: EnsureCreated would skip a context whenever
            // another one's tables already exist, so create each model's tables explicitly.
            await CreateTablesIfMissing(outfit, ct);
            await CreateTablesIfMissing(identity, ct);
        }
        else
        {
            await outfit.Database.MigrateAsync(ct);
            await identity.Database.MigrateAsync(ct);
        }
    }

    public static async Task CreateTablesIfMissing(DbContext ctx, CancellationToken ct)
    {
        var creator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)ctx.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabaseCreator>();
        try { await creator.CreateTablesAsync(ct); }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) { }
    }
}
