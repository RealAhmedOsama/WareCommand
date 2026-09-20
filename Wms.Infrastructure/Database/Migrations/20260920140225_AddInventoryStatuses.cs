using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // Migration data arrays are intentionally materialized inline

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber",
                table: "Stock");

            migrationBuilder.AddColumn<int>(
                name: "InventoryStatusId",
                table: "Stock",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "FromInventoryStatusId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryStatusId",
                table: "Movements",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "StatusChangeLeg",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToInventoryStatusId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    IsAllocatable = table.Column<bool>(type: "boolean", nullable: false),
                    IsPickable = table.Column<bool>(type: "boolean", nullable: false),
                    IsShippable = table.Column<bool>(type: "boolean", nullable: false),
                    IsCountable = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultLocationType = table.Column<int>(type: "integer", nullable: true),
                    ForceForLocationType = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryStatuses_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryStatusTransitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromInventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    ToInventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    RequiresReason = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryStatusTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryStatusTransitions_InventoryStatuses_FromInventoryS~",
                        column: x => x.FromInventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryStatusTransitions_InventoryStatuses_ToInventorySta~",
                        column: x => x.ToInventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "InventoryStatuses",
                columns: new[] { "Id", "Code", "CreatedAt", "DefaultLocationType", "ForceForLocationType", "IsActive", "IsAllocatable", "IsAvailable", "IsCountable", "IsPickable", "IsShippable", "IsSystem", "LocalizedName", "Name", "UpdatedAt", "WarehouseId" },
                values: new object[,]
                {
                    { 1, "AVAILABLE", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, true, true, true, true, true, true, "متاح", "Available", null, null },
                    { 2, "RESERVED", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, false, false, true, true, true, true, "محجوز", "Reserved", null, null },
                    { 3, "QC_PENDING", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, false, false, true, false, false, true, "قيد الفحص", "QC Pending", null, null },
                    { 4, "QUARANTINE", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 12, true, true, false, false, true, false, false, true, "عزل", "Quarantine", null, null },
                    { 5, "HOLD", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, false, false, true, false, false, true, "تعليق", "Hold", null, null },
                    { 6, "DAMAGED", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 13, true, true, false, false, true, false, false, true, "تالف", "Damaged", null, null },
                    { 7, "EXPIRED", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, false, false, true, false, false, true, "منتهي", "Expired", null, null },
                    { 8, "RETURN_PENDING", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 14, true, true, false, false, true, false, false, true, "مرتجع قيد المعالجة", "Return Pending", null, null },
                    { 9, "SCRAP_PENDING", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, false, false, true, false, false, true, "قيد الإهلاك", "Scrap Pending", null, null }
                });

            migrationBuilder.InsertData(
                table: "InventoryStatusTransitions",
                columns: new[] { "Id", "CreatedAt", "FromInventoryStatusId", "IsActive", "RequiresReason", "ToInventoryStatusId", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 2, null },
                    { 2, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 3, null },
                    { 3, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 4, null },
                    { 4, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 5, null },
                    { 5, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 6, null },
                    { 6, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 7, null },
                    { 7, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 8, null },
                    { 8, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 9, null },
                    { 9, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, true, true, 1, null },
                    { 10, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, true, true, 5, null },
                    { 11, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, true, true, 4, null },
                    { 12, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, true, true, 6, null },
                    { 13, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, true, true, 7, null },
                    { 14, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, true, true, 9, null },
                    { 15, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3, true, true, 1, null },
                    { 16, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3, true, true, 4, null },
                    { 17, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3, true, true, 5, null },
                    { 18, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3, true, true, 6, null },
                    { 19, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3, true, true, 7, null },
                    { 20, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3, true, true, 9, null },
                    { 21, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4, true, true, 1, null },
                    { 22, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4, true, true, 5, null },
                    { 23, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4, true, true, 6, null },
                    { 24, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4, true, true, 7, null },
                    { 25, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4, true, true, 9, null },
                    { 26, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5, true, true, 1, null },
                    { 27, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5, true, true, 4, null },
                    { 28, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5, true, true, 6, null },
                    { 29, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5, true, true, 7, null },
                    { 30, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5, true, true, 9, null },
                    { 31, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 6, true, true, 9, null },
                    { 32, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 7, true, true, 9, null },
                    { 33, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 8, true, true, 1, null },
                    { 34, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 8, true, true, 4, null },
                    { 35, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 8, true, true, 5, null },
                    { 36, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 8, true, true, 6, null },
                    { 37, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 8, true, true, 9, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_InventoryStatusId",
                table: "Stock",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusId",
                table: "Stock",
                columns: new[] { "ItemId", "LocationId", "LotId", "SerialNumber", "InventoryStatusId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Movements_FromInventoryStatusId_ToInventoryStatusId",
                table: "Movements",
                columns: new[] { "FromInventoryStatusId", "ToInventoryStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_InventoryStatusId",
                table: "Movements",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ToInventoryStatusId",
                table: "Movements",
                column: "ToInventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStatuses_DefaultLocationType",
                table: "InventoryStatuses",
                column: "DefaultLocationType");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStatuses_IsActive",
                table: "InventoryStatuses",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStatuses_WarehouseId",
                table: "InventoryStatuses",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStatuses_WarehouseId_Code",
                table: "InventoryStatuses",
                columns: new[] { "WarehouseId", "Code" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStatusTransitions_FromInventoryStatusId_ToInventor~",
                table: "InventoryStatusTransitions",
                columns: new[] { "FromInventoryStatusId", "ToInventoryStatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStatusTransitions_IsActive",
                table: "InventoryStatusTransitions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStatusTransitions_ToInventoryStatusId",
                table: "InventoryStatusTransitions",
                column: "ToInventoryStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_InventoryStatuses_FromInventoryStatusId",
                table: "Movements",
                column: "FromInventoryStatusId",
                principalTable: "InventoryStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_InventoryStatuses_InventoryStatusId",
                table: "Movements",
                column: "InventoryStatusId",
                principalTable: "InventoryStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_InventoryStatuses_ToInventoryStatusId",
                table: "Movements",
                column: "ToInventoryStatusId",
                principalTable: "InventoryStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stock_InventoryStatuses_InventoryStatusId",
                table: "Stock",
                column: "InventoryStatusId",
                principalTable: "InventoryStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_InventoryStatuses_FromInventoryStatusId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_InventoryStatuses_InventoryStatusId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_InventoryStatuses_ToInventoryStatusId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Stock_InventoryStatuses_InventoryStatusId",
                table: "Stock");

            migrationBuilder.DropTable(
                name: "InventoryStatusTransitions");

            migrationBuilder.DropTable(
                name: "InventoryStatuses");

            migrationBuilder.DropIndex(
                name: "IX_Stock_InventoryStatusId",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusId",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Movements_FromInventoryStatusId_ToInventoryStatusId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_InventoryStatusId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_ToInventoryStatusId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "InventoryStatusId",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "FromInventoryStatusId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "InventoryStatusId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "StatusChangeLeg",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ToInventoryStatusId",
                table: "Movements");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber",
                table: "Stock",
                columns: new[] { "ItemId", "LocationId", "LotId", "SerialNumber" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
