using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapacityAndSequenceObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CapacityAtObservation",
                table: "ZoneSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CriticalThresholdAtObservation",
                table: "ZoneSnapshots",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WarningThresholdAtObservation",
                table: "ZoneSnapshots",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MissingSequences",
                table: "CameraSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ZoneSnapshots_Capacity",
                table: "ZoneSnapshots",
                sql: "[CapacityAtObservation] IS NULL OR [CapacityAtObservation] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ZoneSnapshots_Capacity",
                table: "ZoneSnapshots");

            migrationBuilder.DropColumn(
                name: "CapacityAtObservation",
                table: "ZoneSnapshots");

            migrationBuilder.DropColumn(
                name: "CriticalThresholdAtObservation",
                table: "ZoneSnapshots");

            migrationBuilder.DropColumn(
                name: "WarningThresholdAtObservation",
                table: "ZoneSnapshots");

            migrationBuilder.DropColumn(
                name: "MissingSequences",
                table: "CameraSnapshots");
        }
    }
}
