using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoLoopOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZoneSnapshots_AnalyticsSessionId_CameraZoneId_SourceTimeMs_Sequence",
                table: "ZoneSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ZoneCounterDeltas_AnalyticsSessionId_ToSourceTimeMs",
                table: "ZoneCounterDeltas");

            migrationBuilder.DropIndex(
                name: "IX_TrackedVisits_AnalyticsSessionId_ResolvedSourceTimeMs",
                table: "TrackedVisits");

            migrationBuilder.DropIndex(
                name: "IX_CameraSnapshots_AnalyticsSessionId_SourceTimeMs_Sequence",
                table: "CameraSnapshots");

            migrationBuilder.AddColumn<long>(
                name: "VideoLoop",
                table: "ZoneSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastVideoLoop",
                table: "ZoneCounterStates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VideoLoop",
                table: "ZoneCounterDeltas",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VideoLoop",
                table: "TrackedVisits",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VideoLoop",
                table: "CameraSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastVideoLoop",
                table: "CameraCurrentStates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastVideoLoop",
                table: "AnalyticsSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSnapshots_AnalyticsSessionId_CameraZoneId_VideoLoop_SourceTimeMs_Sequence",
                table: "ZoneSnapshots",
                columns: new[] { "AnalyticsSessionId", "CameraZoneId", "VideoLoop", "SourceTimeMs", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneCounterDeltas_AnalyticsSessionId_VideoLoop_ToSourceTimeMs",
                table: "ZoneCounterDeltas",
                columns: new[] { "AnalyticsSessionId", "VideoLoop", "ToSourceTimeMs" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedVisits_AnalyticsSessionId_VideoLoop_ResolvedSourceTimeMs",
                table: "TrackedVisits",
                columns: new[] { "AnalyticsSessionId", "VideoLoop", "ResolvedSourceTimeMs" });

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_AnalyticsSessionId_VideoLoop_SourceTimeMs_Sequence",
                table: "CameraSnapshots",
                columns: new[] { "AnalyticsSessionId", "VideoLoop", "SourceTimeMs", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZoneSnapshots_AnalyticsSessionId_CameraZoneId_VideoLoop_SourceTimeMs_Sequence",
                table: "ZoneSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ZoneCounterDeltas_AnalyticsSessionId_VideoLoop_ToSourceTimeMs",
                table: "ZoneCounterDeltas");

            migrationBuilder.DropIndex(
                name: "IX_TrackedVisits_AnalyticsSessionId_VideoLoop_ResolvedSourceTimeMs",
                table: "TrackedVisits");

            migrationBuilder.DropIndex(
                name: "IX_CameraSnapshots_AnalyticsSessionId_VideoLoop_SourceTimeMs_Sequence",
                table: "CameraSnapshots");

            migrationBuilder.DropColumn(
                name: "VideoLoop",
                table: "ZoneSnapshots");

            migrationBuilder.DropColumn(
                name: "LastVideoLoop",
                table: "ZoneCounterStates");

            migrationBuilder.DropColumn(
                name: "VideoLoop",
                table: "ZoneCounterDeltas");

            migrationBuilder.DropColumn(
                name: "VideoLoop",
                table: "TrackedVisits");

            migrationBuilder.DropColumn(
                name: "VideoLoop",
                table: "CameraSnapshots");

            migrationBuilder.DropColumn(
                name: "LastVideoLoop",
                table: "CameraCurrentStates");

            migrationBuilder.DropColumn(
                name: "LastVideoLoop",
                table: "AnalyticsSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSnapshots_AnalyticsSessionId_CameraZoneId_SourceTimeMs_Sequence",
                table: "ZoneSnapshots",
                columns: new[] { "AnalyticsSessionId", "CameraZoneId", "SourceTimeMs", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneCounterDeltas_AnalyticsSessionId_ToSourceTimeMs",
                table: "ZoneCounterDeltas",
                columns: new[] { "AnalyticsSessionId", "ToSourceTimeMs" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedVisits_AnalyticsSessionId_ResolvedSourceTimeMs",
                table: "TrackedVisits",
                columns: new[] { "AnalyticsSessionId", "ResolvedSourceTimeMs" });

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_AnalyticsSessionId_SourceTimeMs_Sequence",
                table: "CameraSnapshots",
                columns: new[] { "AnalyticsSessionId", "SourceTimeMs", "Sequence" });
        }
    }
}
