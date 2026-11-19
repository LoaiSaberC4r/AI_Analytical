using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteFloorLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteFloors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteFloors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteFloors_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZoneSpatialLayouts",
                columns: table => new
                {
                    SiteFloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CameraZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerticesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneSpatialLayouts", x => new { x.SiteFloorId, x.CameraZoneId });
                    table.CheckConstraint("CK_ZoneSpatialLayouts_Geometry", "ISJSON([VerticesJson], ARRAY) = 1");
                    table.ForeignKey(
                        name: "FK_ZoneSpatialLayouts_CameraZones_CameraZoneId",
                        column: x => x.CameraZoneId,
                        principalTable: "CameraZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneSpatialLayouts_SiteFloors_SiteFloorId",
                        column: x => x.SiteFloorId,
                        principalTable: "SiteFloors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteFloors_Name",
                table: "SiteFloors",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_SiteFloors_SiteId_DisplayOrder",
                table: "SiteFloors",
                columns: new[] { "SiteId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSpatialLayouts_CameraZoneId",
                table: "ZoneSpatialLayouts",
                column: "CameraZoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZoneSpatialLayouts");

            migrationBuilder.DropTable(
                name: "SiteFloors");
        }
    }
}
