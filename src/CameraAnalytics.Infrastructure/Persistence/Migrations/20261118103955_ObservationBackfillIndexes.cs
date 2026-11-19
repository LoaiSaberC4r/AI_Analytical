using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObservationBackfillIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ZoneSnapshots_SourceTimestampUtc",
                table: "ZoneSnapshots",
                column: "SourceTimestampUtc",
                filter: "[IsOrdered] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRejections_Topic_ReceivedAtUtc",
                table: "IngestionRejections",
                columns: new[] { "Topic", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_SourceTimestampUtc",
                table: "CameraSnapshots",
                column: "SourceTimestampUtc",
                filter: "[IsOrdered] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZoneSnapshots_SourceTimestampUtc",
                table: "ZoneSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_IngestionRejections_Topic_ReceivedAtUtc",
                table: "IngestionRejections");

            migrationBuilder.DropIndex(
                name: "IX_CameraSnapshots_SourceTimestampUtc",
                table: "CameraSnapshots");
        }
    }
}
