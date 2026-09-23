using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLicensePlateHandlingUnits : Migration
    {
        private static readonly string[] StockIdentityColumns =
            ["ItemId", "LocationId", "LotId", "SerialNumber", "InventoryStatusId", "LicensePlateId"];

        private static readonly string[] LicensePlateContentIdentityColumns =
            ["LicensePlateId", "ItemId", "LotId", "SerialNumberId", "InventoryStatusId", "ItemPackagingId"];

        private static readonly string[] LicensePlateHistoryColumns = ["LicensePlateId", "OccurredAtUtc"];

        private static readonly string[] WarehouseLocationColumns = ["WarehouseId", "CurrentLocationId"];

        private static readonly string[] WarehouseStatusColumns = ["WarehouseId", "Status"];

        private static readonly string[] LegacyStockIdentityColumns =
            ["ItemId", "LocationId", "LotId", "SerialNumber", "InventoryStatusId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusId",
                table: "Stock");

            migrationBuilder.AddColumn<int>(
                name: "LicensePlateId",
                table: "Stock",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentLicensePlateId",
                table: "SerialNumbers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FromLicensePlateId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LicensePlateId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToLicensePlateId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LicensePlateNumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Padding = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlateNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensePlateNumberSequences_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LicensePlates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    CurrentLocationId = table.Column<int>(type: "integer", nullable: true),
                    IsSscc = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ParentLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    GrossWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    LengthCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    WidthCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlates", x => x.Id);
                    table.CheckConstraint("CK_LicensePlates_Dimensions", "(\"GrossWeightKg\" IS NULL OR \"GrossWeightKg\" >= 0) AND (\"LengthCm\" IS NULL OR \"LengthCm\" >= 0) AND (\"WidthCm\" IS NULL OR \"WidthCm\" >= 0) AND (\"HeightCm\" IS NULL OR \"HeightCm\" >= 0)");
                    table.ForeignKey(
                        name: "FK_LicensePlates_LicensePlates_ParentLicensePlateId",
                        column: x => x.ParentLicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlates_Locations_CurrentLocationId",
                        column: x => x.CurrentLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlates_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicensePlateContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    ItemPackagingId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlateContents", x => x.Id);
                    table.CheckConstraint("CK_LicensePlateContents_QuantityPositive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_LicensePlateContents_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateContents_ItemPackagings_ItemPackagingId",
                        column: x => x.ItemPackagingId,
                        principalTable: "ItemPackagings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateContents_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateContents_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateContents_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateContents_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicensePlateHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    FromLocationId = table.Column<int>(type: "integer", nullable: true),
                    ToLocationId = table.Column<int>(type: "integer", nullable: true),
                    FromLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    ToLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlateHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensePlateHistory_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateHistory_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateHistory_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensePlateHistory_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusI~",
                table: "Stock",
                columns: StockIdentityColumns,
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_LicensePlateId",
                table: "Stock",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_CurrentLicensePlateId",
                table: "SerialNumbers",
                column: "CurrentLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_FromLicensePlateId",
                table: "Movements",
                column: "FromLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_LicensePlateId",
                table: "Movements",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ToLicensePlateId",
                table: "Movements",
                column: "ToLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_InventoryStatusId",
                table: "LicensePlateContents",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_ItemId",
                table: "LicensePlateContents",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_ItemPackagingId",
                table: "LicensePlateContents",
                column: "ItemPackagingId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_LicensePlateId_ItemId_LotId_SerialNumb~",
                table: "LicensePlateContents",
                columns: LicensePlateContentIdentityColumns,
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_LotId",
                table: "LicensePlateContents",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_SerialNumberId",
                table: "LicensePlateContents",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateHistory_ItemId",
                table: "LicensePlateHistory",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateHistory_LicensePlateId_OccurredAtUtc",
                table: "LicensePlateHistory",
                columns: LicensePlateHistoryColumns);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateHistory_LotId",
                table: "LicensePlateHistory",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateHistory_ReferenceNumber",
                table: "LicensePlateHistory",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateHistory_SerialNumberId",
                table: "LicensePlateHistory",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateHistory_UserId",
                table: "LicensePlateHistory",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberSequences_WarehouseId",
                table: "LicensePlateNumberSequences",
                column: "WarehouseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlates_CurrentLocationId",
                table: "LicensePlates",
                column: "CurrentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlates_Number",
                table: "LicensePlates",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlates_ParentLicensePlateId",
                table: "LicensePlates",
                column: "ParentLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlates_WarehouseId_CurrentLocationId",
                table: "LicensePlates",
                columns: WarehouseLocationColumns);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlates_WarehouseId_Status",
                table: "LicensePlates",
                columns: WarehouseStatusColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_LicensePlates_FromLicensePlateId",
                table: "Movements",
                column: "FromLicensePlateId",
                principalTable: "LicensePlates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_LicensePlates_LicensePlateId",
                table: "Movements",
                column: "LicensePlateId",
                principalTable: "LicensePlates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_LicensePlates_ToLicensePlateId",
                table: "Movements",
                column: "ToLicensePlateId",
                principalTable: "LicensePlates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SerialNumbers_LicensePlates_CurrentLicensePlateId",
                table: "SerialNumbers",
                column: "CurrentLicensePlateId",
                principalTable: "LicensePlates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stock_LicensePlates_LicensePlateId",
                table: "Stock",
                column: "LicensePlateId",
                principalTable: "LicensePlates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_LicensePlates_FromLicensePlateId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_LicensePlates_LicensePlateId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_LicensePlates_ToLicensePlateId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_SerialNumbers_LicensePlates_CurrentLicensePlateId",
                table: "SerialNumbers");

            migrationBuilder.DropForeignKey(
                name: "FK_Stock_LicensePlates_LicensePlateId",
                table: "Stock");

            migrationBuilder.DropTable(
                name: "LicensePlateContents");

            migrationBuilder.DropTable(
                name: "LicensePlateHistory");

            migrationBuilder.DropTable(
                name: "LicensePlateNumberSequences");

            migrationBuilder.DropTable(
                name: "LicensePlates");

            migrationBuilder.DropIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusI~",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Stock_LicensePlateId",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_SerialNumbers_CurrentLicensePlateId",
                table: "SerialNumbers");

            migrationBuilder.DropIndex(
                name: "IX_Movements_FromLicensePlateId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_LicensePlateId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_ToLicensePlateId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "LicensePlateId",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "CurrentLicensePlateId",
                table: "SerialNumbers");

            migrationBuilder.DropColumn(
                name: "FromLicensePlateId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "LicensePlateId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ToLicensePlateId",
                table: "Movements");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusId",
                table: "Stock",
                columns: LegacyStockIdentityColumns,
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
