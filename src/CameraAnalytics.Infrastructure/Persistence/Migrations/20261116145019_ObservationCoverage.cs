using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObservationCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ObservationEndSourceMs",
                table: "ZoneSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ObservationEndUtc",
                table: "ZoneSnapshots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "WrongWayOutDelta",
                table: "ZoneHourlyAggregates",
                type: "decimal(38,0)",
                precision: 38,
                scale: 0,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "WrongWayInDelta",
                table: "ZoneHourlyAggregates",
                type: "decimal(38,0)",
                precision: 38,
                scale: 0,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "ExitDelta",
                table: "ZoneHourlyAggregates",
                type: "decimal(38,0)",
                precision: 38,
                scale: 0,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<double>(
                name: "EstimatedTotalDwellSeconds",
                table: "ZoneHourlyAggregates",
                type: "float",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "EntryDelta",
                table: "ZoneHourlyAggregates",
                type: "decimal(38,0)",
                precision: 38,
                scale: 0,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "DeparturesDelta",
                table: "ZoneHourlyAggregates",
                type: "decimal(38,0)",
                precision: 38,
                scale: 0,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "ArrivalsDelta",
                table: "ZoneHourlyAggregates",
                type: "decimal(38,0)",
                precision: 38,
                scale: 0,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<DateTime>(
                name: "LatestReportedAtUtc",
                table: "ZoneHourlyAggregates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LatestReportedDwellSeconds",
                table: "ZoneHourlyAggregates",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LongestActiveDwellSeconds",
                table: "ZoneHourlyAggregates",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastReceivedMessageId",
                table: "ZoneCounterStates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "ObservationEndSourceMs",
                table: "CameraSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ObservationEndUtc",
                table: "CameraSnapshots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CameraSnapshots_AnalyticsSessionId_Sequence",
                table: "CameraSnapshots",
                columns: new[] { "AnalyticsSessionId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CameraSnapshots_AnalyticsSessionId_Sequence",
                table: "CameraSnapshots");

            migrationBuilder.DropColumn(
                name: "ObservationEndSourceMs",
                table: "ZoneSnapshots");

            migrationBuilder.DropColumn(
                name: "ObservationEndUtc",
                table: "ZoneSnapshots");

            migrationBuilder.DropColumn(
                name: "LatestReportedAtUtc",
                table: "ZoneHourlyAggregates");

            migrationBuilder.DropColumn(
                name: "LatestReportedDwellSeconds",
                table: "ZoneHourlyAggregates");

            migrationBuilder.DropColumn(
                name: "LongestActiveDwellSeconds",
                table: "ZoneHourlyAggregates");

            migrationBuilder.DropColumn(
                name: "LastReceivedMessageId",
                table: "ZoneCounterStates");

            migrationBuilder.DropColumn(
                name: "ObservationEndSourceMs",
                table: "CameraSnapshots");

            migrationBuilder.DropColumn(
                name: "ObservationEndUtc",
                table: "CameraSnapshots");

            migrationBuilder.AlterColumn<long>(
                name: "WrongWayOutDelta",
                table: "ZoneHourlyAggregates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(38,0)",
                oldPrecision: 38,
                oldScale: 0);

            migrationBuilder.AlterColumn<long>(
                name: "WrongWayInDelta",
                table: "ZoneHourlyAggregates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(38,0)",
                oldPrecision: 38,
                oldScale: 0);

            migrationBuilder.AlterColumn<long>(
                name: "ExitDelta",
                table: "ZoneHourlyAggregates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(38,0)",
                oldPrecision: 38,
                oldScale: 0);

            migrationBuilder.AlterColumn<double>(
                name: "EstimatedTotalDwellSeconds",
                table: "ZoneHourlyAggregates",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "float",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "EntryDelta",
                table: "ZoneHourlyAggregates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(38,0)",
                oldPrecision: 38,
                oldScale: 0);

            migrationBuilder.AlterColumn<long>(
                name: "DeparturesDelta",
                table: "ZoneHourlyAggregates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(38,0)",
                oldPrecision: 38,
                oldScale: 0);

            migrationBuilder.AlterColumn<long>(
                name: "ArrivalsDelta",
                table: "ZoneHourlyAggregates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(38,0)",
                oldPrecision: 38,
                oldScale: 0);
        }
    }
}
