using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableAnalyticsAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Threshold = table.Column<double>(type: "float", nullable: false),
                    MinimumObservedSeconds = table.Column<int>(type: "int", nullable: false),
                    RecoverySeconds = table.Column<int>(type: "int", nullable: false),
                    WindowSeconds = table.Column<int>(type: "int", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: false),
                    MinimumCoveragePercent = table.Column<double>(type: "float", nullable: false),
                    AllowVideo = table.Column<bool>(type: "bit", nullable: false),
                    LastResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertRules_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertRules_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertRules_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VideoLoop = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalyticsAlerts_AlertRules_AlertRuleId",
                        column: x => x.AlertRuleId,
                        principalTable: "AlertRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_CameraId",
                table: "AlertRules",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_CameraZoneId",
                table: "AlertRules",
                column: "CameraZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Name",
                table: "AlertRules",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_SiteId_IsActive",
                table: "AlertRules",
                columns: new[] { "SiteId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAlerts_AlertRuleId",
                table: "AnalyticsAlerts",
                column: "AlertRuleId",
                unique: true,
                filter: "[ResolvedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAlerts_ResolvedAtUtc",
                table: "AnalyticsAlerts",
                column: "ResolvedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAlerts_SiteId_OpenedAtUtc",
                table: "AnalyticsAlerts",
                columns: new[] { "SiteId", "OpenedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsAlerts");

            migrationBuilder.DropTable(
                name: "AlertRules");
        }
    }
}
