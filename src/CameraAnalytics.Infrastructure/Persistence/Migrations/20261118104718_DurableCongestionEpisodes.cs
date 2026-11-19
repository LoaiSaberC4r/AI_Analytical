using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableCongestionEpisodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CongestionProgressJson",
                table: "ZoneCurrentStates",
                type: "nvarchar(1536)",
                maxLength: 1536,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CongestionEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalyticsSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VideoLoop = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    EndSourceTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    ObservedAboveWarningSeconds = table.Column<double>(type: "float", nullable: false),
                    ObservedAboveCriticalSeconds = table.Column<double>(type: "float", nullable: false),
                    WarningThresholdPercent = table.Column<double>(type: "float", nullable: false),
                    CriticalThresholdPercent = table.Column<double>(type: "float", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    MinimumObservedSeconds = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ResolutionReason = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongestionEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CongestionEpisodes_AnalyticsSessions_AnalyticsSessionId",
                        column: x => x.AnalyticsSessionId,
                        principalTable: "AnalyticsSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CongestionEpisodes_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CongestionEpisodes_AnalyticsSessionId_VideoLoop_StartSourceTimeMs",
                table: "CongestionEpisodes",
                columns: new[] { "AnalyticsSessionId", "VideoLoop", "StartSourceTimeMs" });

            migrationBuilder.CreateIndex(
                name: "IX_CongestionEpisodes_CameraZoneId_StartedAtUtc",
                table: "CongestionEpisodes",
                columns: new[] { "CameraZoneId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CongestionEpisodes_EndedAtUtc",
                table: "CongestionEpisodes",
                column: "EndedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CongestionEpisodes");

            migrationBuilder.DropColumn(
                name: "CongestionProgressJson",
                table: "ZoneCurrentStates");
        }
    }
}
