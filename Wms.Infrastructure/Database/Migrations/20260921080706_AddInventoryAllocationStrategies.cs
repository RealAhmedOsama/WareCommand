using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAllocationStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllocationStrategy",
                table: "WarehouseWorks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllocationStrategyKey",
                table: "WarehouseWorks",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllocationStrategyPolicyId",
                table: "WarehouseWorks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AllocationStrategyRevision",
                table: "WarehouseWorks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllocationStrategy",
                table: "InventoryReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllocationStrategyFixedLocationId",
                table: "InventoryReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllocationStrategyKey",
                table: "InventoryReservations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllocationStrategyMinimumShelfLifeDays",
                table: "InventoryReservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AllocationStrategyMissingExpiryFallback",
                table: "InventoryReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllocationStrategyPolicyId",
                table: "InventoryReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllocationStrategyPreferWholeLicensePlate",
                table: "InventoryReservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "AllocationStrategyRevision",
                table: "InventoryReservations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryAllocationStrategyPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    ItemCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DemandType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Strategy = table.Column<int>(type: "integer", nullable: false),
                    FixedLocationId = table.Column<int>(type: "integer", nullable: true),
                    PreferWholeLicensePlate = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumShelfLifeDays = table.Column<int>(type: "integer", nullable: false),
                    MissingExpiryFallback = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAllocationStrategyPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAllocationStrategyPolicies_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAllocationStrategyPolicies_Locations_WarehouseId_F~",
                        columns: x => new { x.WarehouseId, x.FixedLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAllocationStrategyPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAllocationStrategyPolicies_ItemId",
                table: "InventoryAllocationStrategyPolicies",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAllocationStrategyPolicies_WarehouseId_FixedLocati~",
                table: "InventoryAllocationStrategyPolicies",
                columns: new[] { "WarehouseId", "FixedLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAllocationStrategyPolicies_WarehouseId_IsActive_Ef~",
                table: "InventoryAllocationStrategyPolicies",
                columns: new[] { "WarehouseId", "IsActive", "EffectiveFromUtc", "EffectiveToUtc", "ItemId", "ItemCategory", "DemandType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAllocationStrategyPolicies_WarehouseId_PolicyKey",
                table: "InventoryAllocationStrategyPolicies",
                columns: new[] { "WarehouseId", "PolicyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAllocationStrategyPolicies");

            migrationBuilder.DropColumn(
                name: "AllocationStrategy",
                table: "WarehouseWorks");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyKey",
                table: "WarehouseWorks");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyPolicyId",
                table: "WarehouseWorks");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyRevision",
                table: "WarehouseWorks");

            migrationBuilder.DropColumn(
                name: "AllocationStrategy",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyFixedLocationId",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyKey",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyMinimumShelfLifeDays",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyMissingExpiryFallback",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyPolicyId",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyPreferWholeLicensePlate",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "AllocationStrategyRevision",
                table: "InventoryReservations");
        }
    }
}

#pragma warning restore CA1861
