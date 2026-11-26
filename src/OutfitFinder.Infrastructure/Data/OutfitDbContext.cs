using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OutfitFinder.Core.Entities;

namespace OutfitFinder.Infrastructure.Data;

public class OutfitDbContext(DbContextOptions<OutfitDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Visitor> Visitors => Set<Visitor>();
    public DbSet<OutfitSearch> Searches => Set<OutfitSearch>();
    public DbSet<OutfitItem> Items => Set<OutfitItem>();
    public DbSet<ItemMatch> Matches => Set<ItemMatch>();
    public DbSet<ModerationEvent> ModerationEvents => Set<ModerationEvent>();
    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();
    public DbSet<MeasurementProfile> MeasurementProfiles => Set<MeasurementProfile>();
    public DbSet<PatternJob> PatternJobs => Set<PatternJob>();
    public DbSet<MaterialLine> MaterialLines => Set<MaterialLine>();
    public DbSet<AiProviderSetting> AiProviderSettings => Set<AiProviderSetting>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ProblemReport> ProblemReports => Set<ProblemReport>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Plan: Guid as char(36), UTC datetime(6), enums as strings.
        b.Properties<Guid>().HaveColumnType("char(36)");
        b.Properties<DateTime>().HaveColumnType("datetime(6)").HaveConversion<UtcDateTimeConverter>();
        b.Properties<decimal>().HavePrecision(12, 4);
        foreach (var e in typeof(SearchStatus).Assembly.GetTypes().Where(t => t.IsEnum && t.Namespace == typeof(SearchStatus).Namespace))
            b.Properties(e).HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Visitor>(e => { e.HasIndex(x => x.UserId); e.Property(x => x.UserId).HasMaxLength(64); });

        m.Entity<OutfitSearch>(e =>
        {
            e.ToTable("OutfitSearches");
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.ImageHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.IpHash).HasMaxLength(64);
            e.Property(x => x.PendingBlobPath).HasMaxLength(256);
            e.Property(x => x.BlurredBlobPath).HasMaxLength(256);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.Provider).HasMaxLength(32);
            e.Property(x => x.Model).HasMaxLength(128);
            e.Property(x => x.RowVersion).IsConcurrencyToken();
            e.HasIndex(x => new { x.Status, x.CreatedUtc });
            e.HasIndex(x => x.VisitorId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ImageHash);
            e.HasIndex(x => x.CreatedUtc);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OutfitSearchId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<OutfitItem>(e =>
        {
            e.ToTable("OutfitItems");
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.BrandHints).HasMaxLength(256);
            e.Property(x => x.MakeItJson).HasColumnType("text");
            e.HasMany(x => x.Matches).WithOne().HasForeignKey(x => x.OutfitItemId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<ItemMatch>(e =>
        {
            e.ToTable("ItemMatches");
            e.Property(x => x.Store).HasMaxLength(128);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.ImageUrl).HasMaxLength(2048);
        });

        m.Entity<ModerationEvent>(e =>
        {
            e.Property(x => x.ImageHash).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(200);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.IpHash).HasMaxLength(64);
            e.HasIndex(x => x.CreatedUtc);
        });

        m.Entity<ConsentRecord>(e =>
        {
            e.Property(x => x.Document).HasMaxLength(32);
            e.Property(x => x.Version).HasMaxLength(16);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.HasIndex(x => x.UserId);
        });

        m.Entity<MeasurementProfile>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.MeasurementsJson).HasColumnType("text");
        });

        m.Entity<PatternJob>(e =>
        {
            e.Property(x => x.Block).HasMaxLength(32);
            e.Property(x => x.OptionsJson).HasColumnType("text");
            e.Property(x => x.MeasurementsSnapshot).HasColumnType("text");
            e.Property(x => x.PdfBlobPath).HasMaxLength(256);
            e.Property(x => x.A0PdfBlobPath).HasMaxLength(256);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasOne<OutfitItem>().WithMany().HasForeignKey(x => x.OutfitItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Materials).WithOne().HasForeignKey(x => x.PatternJobId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<MaterialLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Url).HasMaxLength(2048);
        });

        m.Entity<AiProviderSetting>(e =>
        {
            e.Property(x => x.Model).HasMaxLength(128);
            e.Property(x => x.Deployment).HasMaxLength(128);
            e.Property(x => x.Endpoint).HasMaxLength(512);
            e.Property(x => x.EncryptedKey).HasColumnType("text");
            e.Property(x => x.KeyLast4).HasMaxLength(4);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
        });

        m.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Actor).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(1000);
            e.HasIndex(x => x.CreatedUtc);
        });

        m.Entity<ProblemReport>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.ContactEmail).HasMaxLength(256);
            e.Property(x => x.ContentUrl).HasMaxLength(2048);
            e.Property(x => x.Reason).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(4000);
        });

        m.Entity<DataProtectionKey>(e =>
        {
            e.Property(x => x.FriendlyName).HasMaxLength(256);
            e.Property(x => x.Xml).HasColumnType("text");
        });
    }
}

/// <summary>Stores and reads every DateTime as UTC.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
