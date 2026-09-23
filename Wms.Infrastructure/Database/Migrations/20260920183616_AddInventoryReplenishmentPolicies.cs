using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReplenishmentPolicies : Migration
    {
        private static readonly string[] LocationPrincipalColumns = ["WarehouseId", "Id"];
        private static readonly string[] ActiveEffectiveIndexColumns =
            ["WarehouseId", "ItemId", "IsActive", "EffectiveFromUtc", "EffectiveToUtc"];
        private static readonly string[] LocationEffectiveIndexColumns =
            ["WarehouseId", "ItemId", "LocationId", "EffectiveFromUtc"];
        private static readonly string[] WarehouseLocationIndexColumns = ["WarehouseId", "LocationId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryReplenishmentPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    MinimumQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    MaximumQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    SafetyStockQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReorderPointQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    TargetQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    QuantityBasis = table.Column<int>(type: "integer", nullable: false),
                    PreferredSource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReplenishmentPolicies", x => x.Id);
                    table.CheckConstraint("CK_InventoryReplenishmentPolicies_QuantityOrder", "\"MinimumQuantity\" >= 0 AND \"SafetyStockQuantity\" >= 0 AND \"ReorderPointQuantity\" >= \"SafetyStockQuantity\" AND \"TargetQuantity\" >= \"ReorderPointQuantity\" AND \"MaximumQuantity\" >= \"TargetQuantity\"");
                    table.ForeignKey(
                        name: "FK_InventoryReplenishmentPolicies_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReplenishmentPolicies_Locations_WarehouseId_Locati~",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: LocationPrincipalColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReplenishmentPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReplenishmentPolicies_ItemId",
                table: "InventoryReplenishmentPolicies",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReplenishmentPolicies_WarehouseId_ItemId_IsActive_~",
                table: "InventoryReplenishmentPolicies",
                columns: ActiveEffectiveIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReplenishmentPolicies_WarehouseId_ItemId_LocationI~",
                table: "InventoryReplenishmentPolicies",
                columns: LocationEffectiveIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReplenishmentPolicies_WarehouseId_LocationId",
                table: "InventoryReplenishmentPolicies",
                columns: WarehouseLocationIndexColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryReplenishmentPolicies");
        }
    }
}
