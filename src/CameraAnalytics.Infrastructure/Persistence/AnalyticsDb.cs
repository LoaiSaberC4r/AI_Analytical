using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CameraAnalytics.Infrastructure.Persistence;

public sealed class AnalyticsDb(DbContextOptions<AnalyticsDb> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<CameraZone> CameraZones => Set<CameraZone>();
    public DbSet<AnalyticsSession> AnalyticsSessions => Set<AnalyticsSession>();
    public DbSet<ReceivedAnalyticsMessage> ReceivedAnalyticsMessages => Set<ReceivedAnalyticsMessage>();
    public DbSet<IngestionRejection> IngestionRejections => Set<IngestionRejection>();
    public DbSet<CameraSnapshot> CameraSnapshots => Set<CameraSnapshot>();
    public DbSet<ZoneSnapshot> ZoneSnapshots => Set<ZoneSnapshot>();
    public DbSet<CameraCurrentState> CameraCurrentStates => Set<CameraCurrentState>();
    public DbSet<ZoneCurrentState> ZoneCurrentStates => Set<ZoneCurrentState>();
    public DbSet<ZoneCounterState> ZoneCounterStates => Set<ZoneCounterState>();
    public DbSet<ZoneCounterDelta> ZoneCounterDeltas => Set<ZoneCounterDelta>();
    public DbSet<TrackedVisit> TrackedVisits => Set<TrackedVisit>();
    public DbSet<ZoneHourlyAggregate> ZoneHourlyAggregates => Set<ZoneHourlyAggregate>();
    public DbSet<DirtyZoneHour> DirtyZoneHours => Set<DirtyZoneHour>();
    public DbSet<ZoneMinuteAggregate> ZoneMinuteAggregates => Set<ZoneMinuteAggregate>();
    public DbSet<CameraMinuteAggregate> CameraMinuteAggregates => Set<CameraMinuteAggregate>();
    public DbSet<DirtyZoneMinute> DirtyZoneMinutes => Set<DirtyZoneMinute>();
    public DbSet<DirtyCameraMinute> DirtyCameraMinutes => Set<DirtyCameraMinute>();
    public DbSet<MinuteBackfillState> MinuteBackfills => Set<MinuteBackfillState>();
    public DbSet<SiteFloor> SiteFloors => Set<SiteFloor>();
    public DbSet<ZoneSpatialLayout> ZoneSpatialLayouts => Set<ZoneSpatialLayout>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AnalyticsAlert> AnalyticsAlerts => Set<AnalyticsAlert>();
    public DbSet<CongestionEpisode> CongestionEpisodes => Set<CongestionEpisode>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.UseCollation("Latin1_General_100_BIN2");
        ConfigureCatalog<Site>(model);
        ConfigureCatalog<Camera>(model);
        ConfigureCatalog<CameraZone>(model);
        ConfigureCatalog<SiteFloor>(model);
        ConfigureCatalog<AlertRule>(model);
        model.Entity<AlertRule>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.Severity).HasMaxLength(16);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId);
            e.HasOne<Camera>().WithMany().HasForeignKey(x => x.CameraId);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
            e.HasIndex(x => new { x.SiteId, x.IsActive });
        });
        model.Entity<AnalyticsAlert>(e =>
        {
            e.HasOne<AlertRule>().WithMany().HasForeignKey(x => x.AlertRuleId);
            e.HasIndex(x => x.AlertRuleId).IsUnique().HasFilter("[ResolvedAtUtc] IS NULL");
            e.HasIndex(x => new { x.SiteId, x.OpenedAtUtc });
            e.HasIndex(x => x.ResolvedAtUtc);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(256);
            e.Property(x => x.ResolvedBy).HasMaxLength(256);
            e.Property(x => x.ResolutionReason).HasMaxLength(64);
        });
        model.Entity<SiteFloor>(e =>
        {
            e.HasIndex(x => new { x.SiteId, x.DisplayOrder });
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId);
        });
        model.Entity<ZoneSpatialLayout>(e =>
        {
            e.HasKey(x => new { x.SiteFloorId, x.CameraZoneId });
            e.HasOne<SiteFloor>().WithMany().HasForeignKey(x => x.SiteFloorId);
            e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.CameraZoneId);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.ToTable(t => t.HasCheckConstraint("CK_ZoneSpatialLayouts_Geometry", "ISJSON([VerticesJson], ARRAY) = 1"));
        });
        model.Entity<Site>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.TimeZoneId).HasMaxLength(128);
            e.HasIndex(x => x.Code).IsUnique();
        });
        model.Entity<Camera>(e =>
        {
            e.Property(x => x.ExternalCameraId).HasMaxLength(Identifiers.ExternalIdLength);
            e.HasIndex(x => x.ExternalCameraId).IsUnique();
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
        });
        model.Entity<CameraZone>(e =>
        {
            e.Property(x => x.ExternalZoneId).HasMaxLength(Identifiers.ExternalIdLength);
            e.HasIndex(x => new { x.CameraId, x.ExternalZoneId }).IsUnique();
            e.HasOne(x => x.Camera).WithMany().HasForeignKey(x => x.CameraId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_CameraZones_Capacity", "[Capacity] IS NULL OR [Capacity] > 0");
                t.HasCheckConstraint("CK_CameraZones_Thresholds",
                    "[WarningThresholdPercent] >= 0 AND [WarningThresholdPercent] < [CriticalThresholdPercent]");
            });
        });
        model.Entity<AnalyticsSession>(e =>
        {
            e.Property(x => x.ExternalSessionId).HasMaxLength(128);
            e.Property(x => x.SourceType).HasMaxLength(32);
            e.Property(x => x.SourceName).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(32);
            e.HasIndex(x => new { x.CameraId, x.ExternalSessionId }).IsUnique();
            e.HasIndex(x => new { x.CameraId, x.StartedAtUtc });
            e.HasOne(x => x.Camera).WithMany().HasForeignKey(x => x.CameraId);
        });
        model.Entity<ReceivedAnalyticsMessage>(e =>
        {
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.Property(x => x.RejectionReason).HasMaxLength(128);
            e.HasIndex(x => new { x.AnalyticsSessionId, x.Sequence }).IsUnique();
            e.HasOne<AnalyticsSession>().WithMany().HasForeignKey(x => x.AnalyticsSessionId);
        });
        model.Entity<IngestionRejection>(e =>
        {
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(128);
            e.HasIndex(x => x.ReceivedAtUtc);
            e.HasIndex(x => new { x.Topic, x.ReceivedAtUtc });
        });
        model.Entity<CameraSnapshot>(e =>
        {
            e.Property(x => x.AnalyticsStatus).HasMaxLength(32);
            e.Property(x => x.ReportedMetadataJson).HasMaxLength(1024);
            e.HasIndex(x => x.ReceivedMessageId).IsUnique();
            e.HasIndex(x => new { x.CameraId, x.ReceivedAtUtc });
            e.HasIndex(x => new { x.CameraId, x.SourceTimestampUtc });
            e.HasIndex(x => x.SourceTimestampUtc).HasFilter("[IsOrdered] = 1");
            e.HasIndex(x => new { x.AnalyticsSessionId, x.Sequence });
            e.HasIndex(x => new { x.AnalyticsSessionId, x.VideoLoop, x.SourceTimeMs, x.Sequence });
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.AnalyticsSessionId);
            e.HasOne<Camera>().WithMany().HasForeignKey(x => x.CameraId);
            e.HasOne<ReceivedAnalyticsMessage>().WithMany().HasForeignKey(x => x.ReceivedMessageId);
        });
        model.Entity<ZoneSnapshot>(e =>
        {
            e.Property(x => x.ReportedMetadataJson).HasMaxLength(1024);
            e.ToTable(t => t.HasCheckConstraint("CK_ZoneSnapshots_Capacity", "[CapacityAtObservation] IS NULL OR [CapacityAtObservation] > 0"));
            e.HasIndex(x => new { x.ReceivedMessageId, x.CameraZoneId }).IsUnique();
            e.HasIndex(x => new { x.CameraZoneId, x.ReceivedAtUtc });
            e.HasIndex(x => new { x.CameraZoneId, x.SourceTimestampUtc });
            e.HasIndex(x => x.SourceTimestampUtc).HasFilter("[IsOrdered] = 1");
            e.HasIndex(x => new { x.AnalyticsSessionId, x.CameraZoneId, x.VideoLoop, x.SourceTimeMs, x.Sequence });
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.AnalyticsSessionId);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
            e.HasOne<ReceivedAnalyticsMessage>().WithMany().HasForeignKey(x => x.ReceivedMessageId);
        });
        model.Entity<CameraCurrentState>(e =>
        {
            e.HasKey(x => x.CameraId);
            e.Property(x => x.AnalyticsStatus).HasMaxLength(32);
            e.Property(x => x.SourceType).HasMaxLength(32);
            e.HasOne(x => x.Camera).WithMany().HasForeignKey(x => x.CameraId);
            e.HasOne<AnalyticsSession>().WithMany().HasForeignKey(x => x.AnalyticsSessionId);
        });
        model.Entity<ZoneCurrentState>(e =>
        {
            e.HasKey(x => x.CameraZoneId);
            e.Property(x => x.CongestionProgressJson).HasMaxLength(1536);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
            e.HasOne<AnalyticsSession>().WithMany().HasForeignKey(x => x.AnalyticsSessionId);
        });
        model.Entity<CongestionEpisode>(e =>
        {
            e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.CameraZoneId);
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.AnalyticsSessionId);
            e.HasIndex(x => new { x.CameraZoneId, x.StartedAtUtc });
            e.HasIndex(x => new { x.AnalyticsSessionId, x.VideoLoop, x.StartSourceTimeMs });
            e.HasIndex(x => x.EndedAtUtc);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ResolutionReason).HasMaxLength(48);
        });
        model.Entity<ZoneCounterState>(e =>
        {
            e.HasKey(x => new { x.AnalyticsSessionId, x.CameraZoneId });
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
            e.HasOne<AnalyticsSession>().WithMany().HasForeignKey(x => x.AnalyticsSessionId);
        });
        model.Entity<ZoneCounterDelta>(e =>
        {
            e.HasIndex(x => new { x.ReceivedMessageId, x.CameraZoneId }).IsUnique();
            e.HasIndex(x => new { x.CameraZoneId, x.EventTimeUtc });
            e.HasIndex(x => new { x.AnalyticsSessionId, x.VideoLoop, x.ToSourceTimeMs });
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.AnalyticsSessionId);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
            e.HasOne<ReceivedAnalyticsMessage>().WithMany().HasForeignKey(x => x.ReceivedMessageId);
        });
        model.Entity<TrackedVisit>(e =>
        {
            e.Property(x => x.CompletionReason).HasMaxLength(64);
            e.HasIndex(x => new { x.AnalyticsSessionId, x.CameraZoneId, x.Status });
            e.HasIndex(x => new { x.CameraZoneId, x.Status, x.ResolvedAtUtc });
            e.HasIndex(x => new { x.AnalyticsSessionId, x.VideoLoop, x.ResolvedSourceTimeMs });
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.AnalyticsSessionId);
            e.HasOne<Camera>().WithMany().HasForeignKey(x => x.CameraId);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
        });
        model.Entity<ZoneHourlyAggregate>(e =>
        {
            e.HasBaseType((Type?)null);
            e.Property(x => x.SourceType).HasMaxLength(32);
            e.Property(x => x.DataQuality).HasMaxLength(32);
            e.Property(x => x.ArrivalsDelta).HasPrecision(38, 0);
            e.Property(x => x.DeparturesDelta).HasPrecision(38, 0);
            e.Property(x => x.EntryDelta).HasPrecision(38, 0);
            e.Property(x => x.ExitDelta).HasPrecision(38, 0);
            e.Property(x => x.WrongWayInDelta).HasPrecision(38, 0);
            e.Property(x => x.WrongWayOutDelta).HasPrecision(38, 0);
            e.HasIndex(x => new { x.CameraZoneId, x.HourStartUtc, x.SourceType }).IsUnique();
            e.HasIndex(x => x.HourStartUtc);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
        });
        model.Entity<ZoneMinuteAggregate>(e =>
        {
            e.HasBaseType((Type?)null);
            e.HasKey(x => new { x.CameraZoneId, x.MinuteStartUtc });
            e.HasIndex(x => x.MinuteStartUtc);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
            e.Property(x => x.DataQuality).HasMaxLength(32);
            e.Property(x => x.ArrivalsDelta).HasPrecision(38, 0);
            e.Property(x => x.DeparturesDelta).HasPrecision(38, 0);
            e.Property(x => x.EntryDelta).HasPrecision(38, 0);
            e.Property(x => x.ExitDelta).HasPrecision(38, 0);
            e.Property(x => x.WrongWayInDelta).HasPrecision(38, 0);
            e.Property(x => x.WrongWayOutDelta).HasPrecision(38, 0);
        });
        model.Entity<CameraMinuteAggregate>(e =>
        {
            e.HasKey(x => new { x.CameraId, x.MinuteStartUtc });
            e.HasIndex(x => x.MinuteStartUtc);
            e.HasOne<Camera>().WithMany().HasForeignKey(x => x.CameraId);
            e.Property(x => x.MissingSequences).HasPrecision(38, 0);
        });
        model.Entity<DirtyZoneMinute>(e =>
        {
            e.HasKey(x => new { x.CameraZoneId, x.MinuteStartUtc });
            e.HasIndex(x => new { x.MinuteStartUtc, x.DirtyAtUtc });
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
        });
        model.Entity<DirtyCameraMinute>(e =>
        {
            e.HasKey(x => new { x.CameraId, x.MinuteStartUtc });
            e.HasIndex(x => new { x.MinuteStartUtc, x.DirtyAtUtc });
            e.HasOne<Camera>().WithMany().HasForeignKey(x => x.CameraId);
        });
        model.Entity<MinuteBackfillState>().Property(x => x.Id).ValueGeneratedNever();
        model.Entity<DirtyZoneHour>(e =>
        {
            e.Property(x => x.SourceType).HasMaxLength(32);
            e.HasKey(x => new { x.CameraZoneId, x.SourceType, x.HourStartUtc });
            e.HasIndex(x => x.DirtyAtUtc);
            e.HasOne<CameraZone>().WithMany().HasForeignKey(x => x.CameraZoneId);
        });

        foreach (var foreignKey in model.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
    }

    private static void ConfigureCatalog<T>(ModelBuilder model) where T : CatalogRecord
    {
        var entity = model.Entity<T>();
        entity.HasBaseType((Type?)null);
        entity.Property(x => x.Name).HasMaxLength(160);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.Name);
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
