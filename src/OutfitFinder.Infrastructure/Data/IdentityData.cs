using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OutfitFinder.Infrastructure.Data;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>ASP.NET Identity tables, in the same MySQL database with their own migrations history.</summary>
public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // MySQL utf8mb4 index limit: keep unbounded strings (keys) at 255 characters.
        b.Properties<string>().HaveMaxLength(255);
        b.Properties<DateTime>().HaveColumnType("datetime(6)").HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().ToTable("AspNetUsers");
    }
}

public static class Roles
{
    public const string Admin = "Admin";
}
