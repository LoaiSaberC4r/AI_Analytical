using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveMinuteAggregation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CameraMinuteAggregates",
                columns: table => new
                {
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinuteStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ObservationDurationSeconds = table.Column<double>(type: "float", nullable: false),
                    AverageStaffCount = table.Column<double>(type: "float", nullable: true),
                    PeakStaffCount = table.Column<int>(type: "int", nullable: true),
                    MinimumFps = table.Column<double>(type: "float", nullable: true),
                    MaximumFps = table.Column<double>(type: "float", nullable: true),
                    AverageFps = table.Column<double>(type: "float", nullable: true),
                    MissingSequences = table.Column<decimal>(type: "decimal(38,0)", precision: 38, scale: 0, nullable: true),
                    GapCount = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraMinuteAggregates", x => new { x.CameraId, x.MinuteStartUtc });
                    table.ForeignKey(
                        name: "FK_CameraMinuteAggregates_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirtyCameraMinutes",
                columns: table => new
                {
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinuteStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DirtyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirtyCameraMinutes", x => new { x.CameraId, x.MinuteStartUtc });
                    table.ForeignKey(
                        name: "FK_DirtyCameraMinutes_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirtyZoneMinutes",
                columns: table => new
                {
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinuteStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DirtyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirtyZoneMinutes", x => new { x.CameraZoneId, x.MinuteStartUtc });
                    table.ForeignKey(
                        name: "FK_DirtyZoneMinutes_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MinuteBackfills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NextMinuteUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinuteBackfills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZoneMinuteAggregates",
                columns: table => new
                {
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinuteStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapacityCoveredSeconds = table.Column<double>(type: "float", nullable: true),
                    AverageUtilizationPercent = table.Column<double>(type: "float", nullable: true),
                    AboveWarningSeconds = table.Column<double>(type: "float", nullable: true),
                    AboveCriticalSeconds = table.Column<double>(type: "float", nullable: true),
                    ObservationDurationSeconds = table.Column<double>(type: "float", nullable: false),
                    AverageOccupancy = table.Column<double>(type: "float", nullable: true),
                    PeakOccupancy = table.Column<int>(type: "int", nullable: true),
                    PeakAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArrivalsDelta = table.Column<decimal>(type: "decimal(38,0)", precision: 38, scale: 0, nullable: false),
                    DeparturesDelta = table.Column<decimal>(type: "decimal(38,0)", precision: 38, scale: 0, nullable: false),
                    EntryDelta = table.Column<decimal>(type: "decimal(38,0)", precision: 38, scale: 0, nullable: false),
                    ExitDelta = table.Column<decimal>(type: "decimal(38,0)", precision: 38, scale: 0, nullable: false),
                    WrongWayInDelta = table.Column<decimal>(type: "decimal(38,0)", precision: 38, scale: 0, nullable: false),
                    WrongWayOutDelta = table.Column<decimal>(type: "decimal(38,0)", precision: 38, scale: 0, nullable: false),
                    LatestReportedDwellSeconds = table.Column<double>(type: "float", nullable: true),
                    LatestReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LongestActiveDwellSeconds = table.Column<double>(type: "float", nullable: true),
                    CounterBaselineCount = table.Column<int>(type: "int", nullable: false),
                    CounterResetCount = table.Column<int>(type: "int", nullable: false),
                    GapCount = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    UncertainVisits = table.Column<int>(type: "int", nullable: false),
                    EstimatedCompletedVisits = table.Column<int>(type: "int", nullable: false),
                    EstimatedTotalDwellSeconds = table.Column<double>(type: "float", nullable: true),
                    CoveragePercent = table.Column<double>(type: "float", nullable: false),
                    DataQuality = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneMinuteAggregates", x => new { x.CameraZoneId, x.MinuteStartUtc });
                    table.ForeignKey(
                        name: "FK_ZoneMinuteAggregates_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CameraMinuteAggregates_MinuteStartUtc",
                table: "CameraMinuteAggregates",
                column: "MinuteStartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DirtyCameraMinutes_MinuteStartUtc_DirtyAtUtc",
                table: "DirtyCameraMinutes",
                columns: new[] { "MinuteStartUtc", "DirtyAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DirtyZoneMinutes_MinuteStartUtc_DirtyAtUtc",
                table: "DirtyZoneMinutes",
                columns: new[] { "MinuteStartUtc", "DirtyAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneMinuteAggregates_MinuteStartUtc",
                table: "ZoneMinuteAggregates",
                column: "MinuteStartUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CameraMinuteAggregates");

            migrationBuilder.DropTable(
                name: "DirtyCameraMinutes");

            migrationBuilder.DropTable(
                name: "DirtyZoneMinutes");

            migrationBuilder.DropTable(
                name: "MinuteBackfills");

            migrationBuilder.DropTable(
                name: "ZoneMinuteAggregates");
        }
    }
}
