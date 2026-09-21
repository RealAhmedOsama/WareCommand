using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPutawayRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PutawayRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Strategy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    ItemCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    PackageType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LicensePlateType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: true),
                    LotStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    MinimumTemperatureCelsius = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    MaximumTemperatureCelsius = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    HazardClass = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StorageProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceProcess = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FixedLocationId = table.Column<int>(type: "integer", nullable: true),
                    TargetLocationType = table.Column<int>(type: "integer", nullable: true),
                    TargetStorageProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FallbackLocationId = table.Column<int>(type: "integer", nullable: true),
                    IsSimulation = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PutawayRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PutawayRules_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PutawayRules_Locations_FallbackLocationId",
                        column: x => x.FallbackLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PutawayRules_Locations_FixedLocationId",
                        column: x => x.FixedLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PutawayRules_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PutawayRules_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PutawayRules_FallbackLocationId",
                table: "PutawayRules",
                column: "FallbackLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PutawayRules_FixedLocationId",
                table: "PutawayRules",
                column: "FixedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PutawayRules_ItemId",
                table: "PutawayRules",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PutawayRules_SupplierId",
                table: "PutawayRules",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PutawayRules_WarehouseId_Code",
                table: "PutawayRules",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PutawayRules_WarehouseId_IsActive_IsSimulation_EffectiveFro~",
                table: "PutawayRules",
                columns: new[] { "WarehouseId", "IsActive", "IsSimulation", "EffectiveFromUtc", "EffectiveToUtc", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_PutawayRules_WarehouseId_ItemId_ItemCategory_SourceProcess",
                table: "PutawayRules",
                columns: new[] { "WarehouseId", "ItemId", "ItemCategory", "SourceProcess" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PutawayRules");
        }
    }
}
