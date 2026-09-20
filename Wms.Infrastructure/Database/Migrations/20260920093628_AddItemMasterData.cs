using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddItemMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryOfOrigin",
                table: "Items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomsCode",
                table: "Items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSupplierCode",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentReference",
                table: "Items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "Items",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageReference",
                table: "Items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHazardous",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LeadTimeDays",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                table: "Items",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleStatus",
                table: "Items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "LocalizedDescription",
                table: "Items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LocalizedName",
                table: "Items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumStock",
                table: "Items",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumTemperatureCelsius",
                table: "Items",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumStock",
                table: "Items",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumTemperatureCelsius",
                table: "Items",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeightKg",
                table: "Items",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseUnit",
                table: "Items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PutawayProfile",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "QualityInspectionRequired",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReorderPolicy",
                table: "Items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresExpiry",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SafetyStock",
                table: "Items",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesPrice",
                table: "Items",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesUnit",
                table: "Items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SpecialHandlingRequired",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardCost",
                table: "Items",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageProfile",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TemperatureControlled",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Stock");

            migrationBuilder.AddColumn<bool>(
                name: "UseFefo",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeCubicMeters",
                table: "Items",
                type: "numeric(18,9)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                table: "Items",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemPackagings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitsPerPackage = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    Barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GrossWeightKg = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    LengthCm = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    WidthCm = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPackagings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemPackagings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                UPDATE "Items"
                SET "LocalizedName" = "Name",
                    "LocalizedDescription" = "Description",
                    "PurchaseUnit" = "UnitOfMeasure",
                    "SalesUnit" = "UnitOfMeasure",
                    "LifecycleStatus" = CASE WHEN "IsActive" THEN 'Active' ELSE 'Inactive' END
                WHERE "PurchaseUnit" = ''
                   OR "SalesUnit" = ''
                   OR "LocalizedName" = ''
                   OR "LocalizedDescription" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ItemBarcodes_Barcode",
                table: "ItemBarcodes",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPackagings_Barcode",
                table: "ItemPackagings",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPackagings_ItemId_Code",
                table: "ItemPackagings",
                columns: new[] { "ItemId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemPackagings");

            migrationBuilder.DropIndex(
                name: "IX_ItemBarcodes_Barcode",
                table: "ItemBarcodes");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "CountryOfOrigin",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "CustomsCode",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DefaultSupplierCode",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DocumentReference",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ImageReference",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "IsHazardous",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LeadTimeDays",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LocalizedDescription",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LocalizedName",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "MaximumStock",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "MaximumTemperatureCelsius",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "MinimumStock",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "MinimumTemperatureCelsius",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "NetWeightKg",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PurchaseUnit",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PutawayProfile",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "QualityInspectionRequired",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ReorderPolicy",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RequiresExpiry",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SafetyStock",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SalesPrice",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SalesUnit",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SpecialHandlingRequired",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "StandardCost",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "StorageProfile",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TemperatureControlled",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "UseFefo",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "VolumeCubicMeters",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                table: "Items");
        }
    }
}
