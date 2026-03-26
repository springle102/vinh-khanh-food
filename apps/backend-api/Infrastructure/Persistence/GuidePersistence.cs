using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinhKhanh.BackendApi.Domain.Entities;

namespace VinhKhanh.BackendApi.Infrastructure.Persistence;

public sealed class VinhKhanhFoodGuideDbContext(DbContextOptions<VinhKhanhFoodGuideDbContext> options) : DbContext(options)
{
    public DbSet<GuideAdminUser> AdminUsers => Set<GuideAdminUser>();
    public DbSet<GuideCategory> Categories => Set<GuideCategory>();
    public DbSet<GuidePoi> Pois => Set<GuidePoi>();
    public DbSet<GuidePoiTag> PoiTags => Set<GuidePoiTag>();
    public DbSet<GuidePoiTranslation> PoiTranslations => Set<GuidePoiTranslation>();
    public DbSet<GuidePoiAudioGuide> AudioGuides => Set<GuidePoiAudioGuide>();
    public DbSet<GuideMediaAsset> MediaAssets => Set<GuideMediaAsset>();
    public DbSet<GuideFoodItem> FoodItems => Set<GuideFoodItem>();
    public DbSet<GuideTourRoute> Routes => Set<GuideTourRoute>();
    public DbSet<GuideRouteStop> RouteStops => Set<GuideRouteStop>();
    public DbSet<GuideViewLog> ViewLogs => Set<GuideViewLog>();
    public DbSet<GuideAudioListenLog> AudioListenLogs => Set<GuideAudioListenLog>();
    public DbSet<GuideRefreshSession> RefreshSessions => Set<GuideRefreshSession>();
    public DbSet<GuideSystemSetting> SystemSettings => Set<GuideSystemSetting>();
    public DbSet<GuideSystemSettingLanguage> SystemSettingLanguages => Set<GuideSystemSettingLanguage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VinhKhanhFoodGuideDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class AdminUserEntityConfiguration : IEntityTypeConfiguration<GuideAdminUser>
{
    public void Configure(EntityTypeBuilder<GuideAdminUser> builder)
    {
        builder.ToTable("AdminUsers", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.Name).HasMaxLength(120);
        builder.Property(item => item.Email).HasMaxLength(200);
        builder.Property(item => item.Phone).HasMaxLength(30);
        builder.Property(item => item.Role).HasMaxLength(30);
        builder.Property(item => item.Password).HasColumnName("Password").HasMaxLength(200);
        builder.Property(item => item.Status).HasColumnName("Status").HasMaxLength(30);
        builder.Property(item => item.AvatarColor).HasMaxLength(20);
        builder.Property(item => item.ManagedPoiId).HasMaxLength(50);
    }
}

public sealed class CategoryEntityConfiguration : IEntityTypeConfiguration<GuideCategory>
{
    public void Configure(EntityTypeBuilder<GuideCategory> builder)
    {
        builder.ToTable("Categories", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.Name).HasMaxLength(100);
        builder.Property(item => item.Slug).HasMaxLength(120);
        builder.Property(item => item.Icon).HasMaxLength(50);
        builder.Property(item => item.Color).HasMaxLength(20);
    }
}

public sealed class PoiEntityConfiguration : IEntityTypeConfiguration<GuidePoi>
{
    public void Configure(EntityTypeBuilder<GuidePoi> builder)
    {
        builder.ToTable("Pois", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.Slug).HasMaxLength(150);
        builder.Property(item => item.AddressLine).HasColumnName("AddressLine").HasMaxLength(300);
        builder.Property(item => item.Latitude).HasColumnName("Latitude").HasPrecision(9, 6);
        builder.Property(item => item.Longitude).HasColumnName("Longitude").HasPrecision(9, 6);
        builder.Property(item => item.CategoryId).HasMaxLength(50);
        builder.Property(item => item.Status).HasColumnName("Status").HasMaxLength(30);
        builder.Property(item => item.DefaultLanguageCode).HasMaxLength(20);
        builder.Property(item => item.District).HasMaxLength(100);
        builder.Property(item => item.Ward).HasMaxLength(100);
        builder.Property(item => item.PriceRange).HasMaxLength(100);
        builder.Property(item => item.AverageVisitDurationMinutes).HasColumnName("AverageVisitDurationMinutes");
        builder.Property(item => item.UpdatedBy).HasMaxLength(120);
        builder.Property(item => item.QrCode).HasMaxLength(150);
        builder.Property(item => item.OpeningHours).HasMaxLength(150);

        builder.HasOne(item => item.Category)
            .WithMany(item => item.Pois)
            .HasForeignKey(item => item.CategoryId);

        builder.HasOne(item => item.OwnerUser)
            .WithMany(item => item.ManagedPois)
            .HasForeignKey(item => item.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PoiTagEntityConfiguration : IEntityTypeConfiguration<GuidePoiTag>
{
    public void Configure(EntityTypeBuilder<GuidePoiTag> builder)
    {
        builder.ToTable("PoiTags", "dbo");
        builder.HasKey(item => new { item.PoiId, item.TagValue });
        builder.Property(item => item.PoiId).HasMaxLength(50);
        builder.Property(item => item.TagValue).HasMaxLength(100);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.Tags)
            .HasForeignKey(item => item.PoiId);
    }
}

public sealed class PoiTranslationEntityConfiguration : IEntityTypeConfiguration<GuidePoiTranslation>
{
    public void Configure(EntityTypeBuilder<GuidePoiTranslation> builder)
    {
        builder.ToTable("PoiTranslations", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.EntityType).HasMaxLength(30);
        builder.Property(item => item.EntityId).HasMaxLength(50);
        builder.Property(item => item.LanguageCode).HasMaxLength(20);
        builder.Property(item => item.Title).HasMaxLength(200);
        builder.Property(item => item.SeoTitle).HasMaxLength(200);
        builder.Property(item => item.UpdatedBy).HasMaxLength(120);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.Translations)
            .HasForeignKey(item => item.EntityId);
    }
}

public sealed class PoiAudioGuideEntityConfiguration : IEntityTypeConfiguration<GuidePoiAudioGuide>
{
    public void Configure(EntityTypeBuilder<GuidePoiAudioGuide> builder)
    {
        builder.ToTable("AudioGuides", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.EntityType).HasMaxLength(30);
        builder.Property(item => item.EntityId).HasMaxLength(50);
        builder.Property(item => item.LanguageCode).HasMaxLength(20);
        builder.Property(item => item.AudioUrl).HasMaxLength(500);
        builder.Property(item => item.VoiceType).HasMaxLength(30);
        builder.Property(item => item.SourceType).HasMaxLength(30);
        builder.Property(item => item.Status).HasColumnName("Status").HasMaxLength(30);
        builder.Property(item => item.UpdatedBy).HasMaxLength(120);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.AudioGuides)
            .HasForeignKey(item => item.EntityId);
    }
}

public sealed class MediaAssetEntityConfiguration : IEntityTypeConfiguration<GuideMediaAsset>
{
    public void Configure(EntityTypeBuilder<GuideMediaAsset> builder)
    {
        builder.ToTable("MediaAssets", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.EntityType).HasMaxLength(30);
        builder.Property(item => item.EntityId).HasMaxLength(50);
        builder.Property(item => item.MediaType).HasColumnName("MediaType").HasMaxLength(30);
        builder.Property(item => item.Url).HasMaxLength(500);
        builder.Property(item => item.AltText).HasMaxLength(300);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.MediaAssets)
            .HasForeignKey(item => item.EntityId);
    }
}

public sealed class FoodItemEntityConfiguration : IEntityTypeConfiguration<GuideFoodItem>
{
    public void Configure(EntityTypeBuilder<GuideFoodItem> builder)
    {
        builder.ToTable("FoodItems", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.PoiId).HasMaxLength(50);
        builder.Property(item => item.Name).HasMaxLength(150);
        builder.Property(item => item.PriceRange).HasMaxLength(100);
        builder.Property(item => item.ImageUrl).HasMaxLength(500);
        builder.Property(item => item.SpicyLevel).HasMaxLength(30);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.FoodItems)
            .HasForeignKey(item => item.PoiId);
    }
}

public sealed class RouteEntityConfiguration : IEntityTypeConfiguration<GuideTourRoute>
{
    public void Configure(EntityTypeBuilder<GuideTourRoute> builder)
    {
        builder.ToTable("Routes", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.Name).HasMaxLength(150);
        builder.Property(item => item.Difficulty).HasMaxLength(30);
    }
}

public sealed class RouteStopEntityConfiguration : IEntityTypeConfiguration<GuideRouteStop>
{
    public void Configure(EntityTypeBuilder<GuideRouteStop> builder)
    {
        builder.ToTable("RouteStops", "dbo");
        builder.HasKey(item => new { item.RouteId, item.StopOrder });
        builder.Property(item => item.RouteId).HasMaxLength(50);
        builder.Property(item => item.PoiId).HasMaxLength(50);
        builder.HasOne(item => item.Route)
            .WithMany(item => item.Stops)
            .HasForeignKey(item => item.RouteId);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.RouteStops)
            .HasForeignKey(item => item.PoiId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ViewLogEntityConfiguration : IEntityTypeConfiguration<GuideViewLog>
{
    public void Configure(EntityTypeBuilder<GuideViewLog> builder)
    {
        builder.ToTable("ViewLogs", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.PoiId).HasMaxLength(50);
        builder.Property(item => item.LanguageCode).HasMaxLength(20);
        builder.Property(item => item.DeviceType).HasMaxLength(20);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.ViewLogs)
            .HasForeignKey(item => item.PoiId);
    }
}

public sealed class AudioListenLogEntityConfiguration : IEntityTypeConfiguration<GuideAudioListenLog>
{
    public void Configure(EntityTypeBuilder<GuideAudioListenLog> builder)
    {
        builder.ToTable("AudioListenLogs", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(50);
        builder.Property(item => item.PoiId).HasMaxLength(50);
        builder.Property(item => item.LanguageCode).HasMaxLength(20);
        builder.HasOne(item => item.Poi)
            .WithMany(item => item.AudioListenLogs)
            .HasForeignKey(item => item.PoiId);
    }
}

public sealed class RefreshSessionEntityConfiguration : IEntityTypeConfiguration<GuideRefreshSession>
{
    public void Configure(EntityTypeBuilder<GuideRefreshSession> builder)
    {
        builder.ToTable("RefreshSessions", "dbo");
        builder.HasKey(item => item.RefreshToken);
        builder.Property(item => item.RefreshToken).HasMaxLength(200);
        builder.Property(item => item.UserId).HasMaxLength(50);
        builder.HasOne(item => item.User)
            .WithMany(item => item.RefreshSessions)
            .HasForeignKey(item => item.UserId);
    }
}

public sealed class SystemSettingEntityConfiguration : IEntityTypeConfiguration<GuideSystemSetting>
{
    public void Configure(EntityTypeBuilder<GuideSystemSetting> builder)
    {
        builder.ToTable("SystemSettings", "dbo");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.AppName).HasMaxLength(200);
        builder.Property(item => item.SupportEmail).HasMaxLength(200);
        builder.Property(item => item.DefaultLanguage).HasMaxLength(20);
        builder.Property(item => item.FallbackLanguage).HasMaxLength(20);
        builder.Property(item => item.MapProvider).HasMaxLength(50);
        builder.Property(item => item.StorageProvider).HasMaxLength(50);
        builder.Property(item => item.TtsProvider).HasMaxLength(50);
    }
}

public sealed class SystemSettingLanguageEntityConfiguration : IEntityTypeConfiguration<GuideSystemSettingLanguage>
{
    public void Configure(EntityTypeBuilder<GuideSystemSettingLanguage> builder)
    {
        builder.ToTable("SystemSettingLanguages", "dbo");
        builder.HasKey(item => new { item.SettingId, item.LanguageType, item.LanguageCode });
        builder.Property(item => item.LanguageType).HasMaxLength(20);
        builder.Property(item => item.LanguageCode).HasMaxLength(20);
        builder.HasOne(item => item.Setting)
            .WithMany(item => item.Languages)
            .HasForeignKey(item => item.SettingId);
    }
}

public sealed class FoodGuideDatabaseInitializer(IConfiguration configuration, ILogger<FoodGuideDatabaseInitializer> logger)
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("GuideSqlServer")
            ?? configuration.GetConnectionString("AdminSqlServer")
            ?? throw new InvalidOperationException("Khong tim thay connection string GuideSqlServer/AdminSqlServer.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var statements = new[]
        {
            """
            IF COL_LENGTH(N'dbo.Pois', N'QrCode') IS NULL
                ALTER TABLE dbo.Pois ADD QrCode NVARCHAR(150) NULL;
            """,
            """
            IF COL_LENGTH(N'dbo.Pois', N'OpeningHours') IS NULL
                ALTER TABLE dbo.Pois ADD OpeningHours NVARCHAR(150) NULL;
            """,
            """
            UPDATE dbo.Pois
            SET QrCode = Slug
            WHERE ISNULL(LTRIM(RTRIM(QrCode)), N'') = N'';
            """,
            """
            UPDATE dbo.Pois
            SET OpeningHours = CASE
                WHEN Id = N'poi-bbq-night' THEN N'17:00 - 23:45'
                WHEN Id = N'poi-snail-signature' THEN N'16:30 - 23:30'
                WHEN Id = N'poi-sweet-lane' THEN N'15:00 - 22:30'
                ELSE N'17:00 - 23:00'
            END
            WHERE ISNULL(LTRIM(RTRIM(OpeningHours)), N'') = N'';
            """
        };

        var totalAffectedRows = 0;
        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            totalAffectedRows += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation("FoodGuide schema initializer da chay xong. So dong anh huong: {AffectedRows}", totalAffectedRows);
    }
}

