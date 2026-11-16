using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestionRejections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionRejections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cameras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalCameraId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cameras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cameras_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalSessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    FirstSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    LastSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    FirstSourceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSourceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalyticsSessions_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CameraZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalZoneId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    WarningThresholdPercent = table.Column<double>(type: "float", nullable: false),
                    CriticalThresholdPercent = table.Column<double>(type: "float", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraZones", x => x.Id);
                    table.CheckConstraint("CK_CameraZones_Capacity", "[Capacity] IS NULL OR [Capacity] > 0");
                    table.CheckConstraint("CK_CameraZones_Thresholds", "[WarningThresholdPercent] >= 0 AND [WarningThresholdPercent] < [CriticalThresholdPercent]");
                    table.ForeignKey(
                        name: "FK_CameraZones_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CameraCurrentStates",
                columns: table => new
                {
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    AnalyticsStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProcessingFps = table.Column<double>(type: "float", nullable: false),
                    StaffCount = table.Column<int>(type: "int", nullable: false),
                    LastSourceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    DataFreshness = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraCurrentStates", x => x.CameraId);
                    table.ForeignKey(
                        name: "FK_CameraCurrentStates_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CameraCurrentStates_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedAnalyticsMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SourceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessingStatus = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedAnalyticsMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivedAnalyticsMessages_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirtyZoneHours",
                columns: table => new
                {
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    HourStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DirtyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirtyZoneHours", x => new { x.CameraZoneId, x.SourceType, x.HourStartUtc });
                    table.ForeignKey(
                        name: "FK_DirtyZoneHours_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrackedVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalTrackId = table.Column<long>(type: "bigint", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FirstSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    LastSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    ResolvedSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObservedMissingSeconds = table.Column<double>(type: "float", nullable: false),
                    LastDwellSeconds = table.Column<double>(type: "float", nullable: false),
                    MaximumObservedDwellSeconds = table.Column<double>(type: "float", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CompletionReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EstimatedCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedVisits_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackedVisits_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackedVisits_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZoneCounterStates",
                columns: table => new
                {
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    LastSourceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ZoneArrivals = table.Column<long>(type: "bigint", nullable: false),
                    ZoneDepartures = table.Column<long>(type: "bigint", nullable: false),
                    Entry = table.Column<long>(type: "bigint", nullable: false),
                    Exit = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayIn = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayOut = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneCounterStates", x => new { x.AnalyticsSessionId, x.CameraZoneId });
                    table.ForeignKey(
                        name: "FK_ZoneCounterStates_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneCounterStates_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZoneCurrentStates",
                columns: table => new
                {
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Occupancy = table.Column<int>(type: "int", nullable: false),
                    ZoneArrivals = table.Column<long>(type: "bigint", nullable: false),
                    ZoneDepartures = table.Column<long>(type: "bigint", nullable: false),
                    Entry = table.Column<long>(type: "bigint", nullable: false),
                    Exit = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayIn = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayOut = table.Column<long>(type: "bigint", nullable: false),
                    ReportedAverageDwellSeconds = table.Column<double>(type: "float", nullable: false),
                    ActiveVisitorsCount = table.Column<int>(type: "int", nullable: false),
                    LongestActiveDwellSeconds = table.Column<double>(type: "float", nullable: true),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneCurrentStates", x => x.CameraZoneId);
                    table.ForeignKey(
                        name: "FK_ZoneCurrentStates_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneCurrentStates_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZoneHourlyAggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    HourStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ObservationDurationSeconds = table.Column<double>(type: "float", nullable: false),
                    AverageOccupancy = table.Column<double>(type: "float", nullable: true),
                    PeakOccupancy = table.Column<int>(type: "int", nullable: true),
                    PeakAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArrivalsDelta = table.Column<long>(type: "bigint", nullable: false),
                    DeparturesDelta = table.Column<long>(type: "bigint", nullable: false),
                    EntryDelta = table.Column<long>(type: "bigint", nullable: false),
                    ExitDelta = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayInDelta = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayOutDelta = table.Column<long>(type: "bigint", nullable: false),
                    CounterBaselineCount = table.Column<int>(type: "int", nullable: false),
                    CounterResetCount = table.Column<int>(type: "int", nullable: false),
                    GapCount = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    UncertainVisits = table.Column<int>(type: "int", nullable: false),
                    EstimatedCompletedVisits = table.Column<int>(type: "int", nullable: false),
                    EstimatedTotalDwellSeconds = table.Column<double>(type: "float", nullable: false),
                    CoveragePercent = table.Column<double>(type: "float", nullable: false),
                    DataQuality = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneHourlyAggregates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoneHourlyAggregates_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CameraSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    SourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    SourceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessingFps = table.Column<double>(type: "float", nullable: false),
                    StaffCount = table.Column<int>(type: "int", nullable: false),
                    AnalyticsStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsOrdered = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CameraSnapshots_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CameraSnapshots_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CameraSnapshots_ReceivedAnalyticsMessages_ReceivedMessageId",
                        column: x => x.ReceivedMessageId,
                        principalTable: "ReceivedAnalyticsMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZoneCounterDeltas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    ToSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    ZoneArrivalsDelta = table.Column<long>(type: "bigint", nullable: false),
                    ZoneDeparturesDelta = table.Column<long>(type: "bigint", nullable: false),
                    EntryDelta = table.Column<long>(type: "bigint", nullable: false),
                    ExitDelta = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayInDelta = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayOutDelta = table.Column<long>(type: "bigint", nullable: false),
                    CounterQuality = table.Column<int>(type: "int", nullable: false),
                    ResetMask = table.Column<int>(type: "int", nullable: false),
                    HasObservationGap = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneCounterDeltas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoneCounterDeltas_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneCounterDeltas_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneCounterDeltas_ReceivedAnalyticsMessages_ReceivedMessageId",
                        column: x => x.ReceivedMessageId,
                        principalTable: "ReceivedAnalyticsMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZoneSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    SourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    SourceTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Occupancy = table.Column<int>(type: "int", nullable: false),
                    ZoneArrivals = table.Column<long>(type: "bigint", nullable: false),
                    ZoneDepartures = table.Column<long>(type: "bigint", nullable: false),
                    Entry = table.Column<long>(type: "bigint", nullable: false),
                    Exit = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayIn = table.Column<long>(type: "bigint", nullable: false),
                    WrongWayOut = table.Column<long>(type: "bigint", nullable: false),
                    ReportedAverageDwellSeconds = table.Column<double>(type: "float", nullable: false),
                    ActiveVisitorsCount = table.Column<int>(type: "int", nullable: false),
                    LongestActiveDwellSeconds = table.Column<double>(type: "float", nullable: true),
                    IsOrdered = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoneSnapshots_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneSnapshots_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneSnapshots_ReceivedAnalyticsMessages_ReceivedMessageId",
                        column: x => x.ReceivedMessageId,
                        principalTable: "ReceivedAnalyticsMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsSessions_CameraId_ExternalSessionId",
                table: "AnalyticsSessions",
                columns: new[] { "CameraId", "ExternalSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsSessions_CameraId_StartedAtUtc",
                table: "AnalyticsSessions",
                columns: new[] { "CameraId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CameraCurrentStates_AnalyticsSessionId",
                table: "CameraCurrentStates",
                column: "AnalyticsSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_ExternalCameraId",
                table: "Cameras",
                column: "ExternalCameraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_Name",
                table: "Cameras",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_SiteId",
                table: "Cameras",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_AnalyticsSessionId_SourceTimeMs_Sequence",
                table: "CameraSnapshots",
                columns: new[] { "AnalyticsSessionId", "SourceTimeMs", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_CameraId_ReceivedAtUtc",
                table: "CameraSnapshots",
                columns: new[] { "CameraId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_CameraId_SourceTimestampUtc",
                table: "CameraSnapshots",
                columns: new[] { "CameraId", "SourceTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_ReceivedMessageId",
                table: "CameraSnapshots",
                column: "ReceivedMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CameraZones_CameraId_ExternalZoneId",
                table: "CameraZones",
                columns: new[] { "CameraId", "ExternalZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CameraZones_Name",
                table: "CameraZones",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DirtyZoneHours_DirtyAtUtc",
                table: "DirtyZoneHours",
                column: "DirtyAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRejections_ReceivedAtUtc",
                table: "IngestionRejections",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedAnalyticsMessages_AnalyticsSessionId_Sequence",
                table: "ReceivedAnalyticsMessages",
                columns: new[] { "AnalyticsSessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Code",
                table: "Sites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Name",
                table: "Sites",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedVisits_AnalyticsSessionId_CameraZoneId_Status",
                table: "TrackedVisits",
                columns: new[] { "AnalyticsSessionId", "CameraZoneId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedVisits_AnalyticsSessionId_ResolvedSourceTimeMs",
                table: "TrackedVisits",
                columns: new[] { "AnalyticsSessionId", "ResolvedSourceTimeMs" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedVisits_CameraId",
                table: "TrackedVisits",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedVisits_CameraZoneId_Status_ResolvedAtUtc",
                table: "TrackedVisits",
                columns: new[] { "CameraZoneId", "Status", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneCounterDeltas_AnalyticsSessionId_ToSourceTimeMs",
                table: "ZoneCounterDeltas",
                columns: new[] { "AnalyticsSessionId", "ToSourceTimeMs" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneCounterDeltas_CameraZoneId_EventTimeUtc",
                table: "ZoneCounterDeltas",
                columns: new[] { "CameraZoneId", "EventTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneCounterDeltas_ReceivedMessageId_CameraZoneId",
                table: "ZoneCounterDeltas",
                columns: new[] { "ReceivedMessageId", "CameraZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoneCounterStates_CameraZoneId",
                table: "ZoneCounterStates",
                column: "CameraZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneCurrentStates_AnalyticsSessionId",
                table: "ZoneCurrentStates",
                column: "AnalyticsSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneHourlyAggregates_CameraZoneId_HourStartUtc_SourceType",
                table: "ZoneHourlyAggregates",
                columns: new[] { "CameraZoneId", "HourStartUtc", "SourceType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoneHourlyAggregates_HourStartUtc",
                table: "ZoneHourlyAggregates",
                column: "HourStartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSnapshots_AnalyticsSessionId_CameraZoneId_SourceTimeMs_Sequence",
                table: "ZoneSnapshots",
                columns: new[] { "AnalyticsSessionId", "CameraZoneId", "SourceTimeMs", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSnapshots_CameraZoneId_ReceivedAtUtc",
                table: "ZoneSnapshots",
                columns: new[] { "CameraZoneId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSnapshots_CameraZoneId_SourceTimestampUtc",
                table: "ZoneSnapshots",
                columns: new[] { "CameraZoneId", "SourceTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSnapshots_ReceivedMessageId_CameraZoneId",
                table: "ZoneSnapshots",
                columns: new[] { "ReceivedMessageId", "CameraZoneId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CameraCurrentStates");

            migrationBuilder.DropTable(
                name: "CameraSnapshots");

            migrationBuilder.DropTable(
                name: "DirtyZoneHours");

            migrationBuilder.DropTable(
                name: "IngestionRejections");

            migrationBuilder.DropTable(
                name: "TrackedVisits");

            migrationBuilder.DropTable(
                name: "ZoneCounterDeltas");

            migrationBuilder.DropTable(
                name: "ZoneCounterStates");

            migrationBuilder.DropTable(
                name: "ZoneCurrentStates");

            migrationBuilder.DropTable(
                name: "ZoneHourlyAggregates");

            migrationBuilder.DropTable(
                name: "ZoneSnapshots");

            migrationBuilder.DropTable(
                name: "CameraZones");

            migrationBuilder.DropTable(
                name: "ReceivedAnalyticsMessages");

            migrationBuilder.DropTable(
                name: "AnalyticsSessions");

            migrationBuilder.DropTable(
                name: "Cameras");

            migrationBuilder.DropTable(
                name: "Sites");
        }
    }
}
