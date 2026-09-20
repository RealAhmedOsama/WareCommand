using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseAuthorization : Migration
    {
        private static readonly string[] UserIdIsDefaultColumns = ["UserId", "IsDefault"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsUserWarehouseAssignments",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsUserWarehouseAssignments", x => new { x.UserId, x.WarehouseId });
                    table.ForeignKey(
                        name: "FK_WmsUserWarehouseAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WmsUserWarehouseAssignments_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsUserWarehouseAssignments_UserId_IsDefault",
                table: "WmsUserWarehouseAssignments",
                columns: UserIdIsDefaultColumns);

            migrationBuilder.CreateIndex(
                name: "IX_WmsUserWarehouseAssignments_WarehouseId",
                table: "WmsUserWarehouseAssignments",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsUserWarehouseAssignments");
        }
    }
}
