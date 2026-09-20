using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseMasterData : Migration
    {
        private static readonly string[] LocationWarehouseCodeColumns = ["WarehouseId", "Code"];
        private static readonly string[] OperationalRoleColumns = ["WarehouseId", "Role"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Locations_Code",
                table: "Locations");

            migrationBuilder.AddColumn<bool>(
                name: "AllowNegativeStock",
                table: "Warehouses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ArabicName",
                table: "Warehouses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "BlockExpiredReceipt",
                table: "Warehouses",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Warehouses",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Warehouses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Warehouses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ExpiryWarningDays",
                table: "Warehouses",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "RequireLocationForAdjustment",
                table: "Warehouses",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Warehouses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<bool>(
                name: "WorkflowEnabled",
                table: "Warehouses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WarehouseNumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    NextReceiptNumber = table.Column<long>(type: "bigint", nullable: false),
                    NextOrderNumber = table.Column<long>(type: "bigint", nullable: false),
                    NextWorkNumber = table.Column<long>(type: "bigint", nullable: false),
                    NextShipmentNumber = table.Column<long>(type: "bigint", nullable: false),
                    NextTransferNumber = table.Column<long>(type: "bigint", nullable: false),
                    NextCountNumber = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseNumberSequences_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseOperationalLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseOperationalLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseOperationalLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseOperationalLocations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_WarehouseId_Code",
                table: "Locations",
                columns: LocationWarehouseCodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseNumberSequences_WarehouseId",
                table: "WarehouseNumberSequences",
                column: "WarehouseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseOperationalLocations_LocationId",
                table: "WarehouseOperationalLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseOperationalLocations_WarehouseId_Role",
                table: "WarehouseOperationalLocations",
                columns: OperationalRoleColumns,
                unique: true);

            migrationBuilder.Sql(
                "INSERT INTO \"WarehouseNumberSequences\" (\"WarehouseId\", \"NextReceiptNumber\", \"NextOrderNumber\", \"NextWorkNumber\", \"NextShipmentNumber\", \"NextTransferNumber\", \"NextCountNumber\", \"Revision\", \"CreatedAt\", \"UpdatedAt\") " +
                "SELECT \"Id\", 1, 1, 1, 1, 1, 1, 1, CURRENT_TIMESTAMP, NULL FROM \"Warehouses\" warehouse " +
                "WHERE NOT EXISTS (SELECT 1 FROM \"WarehouseNumberSequences\" sequence WHERE sequence.\"WarehouseId\" = warehouse.\"Id\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WarehouseNumberSequences");

            migrationBuilder.DropTable(
                name: "WarehouseOperationalLocations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_WarehouseId_Code",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "AllowNegativeStock",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ArabicName",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "BlockExpiredReceipt",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ExpiryWarningDays",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "RequireLocationForAdjustment",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "WorkflowEnabled",
                table: "Warehouses");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Code",
                table: "Locations",
                column: "Code",
                unique: true);
        }
    }
}
