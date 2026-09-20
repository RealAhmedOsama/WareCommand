using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationMasterData : Migration
    {
        private static readonly string[] LocationWarehouseIdParentColumns = ["WarehouseId", "ParentLocationId"];
        private static readonly string[] LocationWarehouseIdIdColumns = ["WarehouseId", "Id"];
        private static readonly string[] LocationWarehouseIdBarcodeColumns = ["WarehouseId", "Barcode"];
        private static readonly string[] LocationWarehouseIdParentPriorityCodeColumns = ["WarehouseId", "ParentLocationId", "Priority", "Code"];
        private static readonly string[] LocationWarehouseIdTypeActiveColumns = ["WarehouseId", "Type", "IsActive"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Locations_ParentLocationId",
                table: "Locations");

            migrationBuilder.AddColumn<string>(
                name: "AccessRestriction",
                table: "Locations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "AllowMixedItems",
                table: "Locations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowMixedLots",
                table: "Locations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConstraintAttributesJson",
                table: "Locations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "HazardClass",
                table: "Locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsCountable",
                table: "Locations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLpns",
                table: "Locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxPallets",
                table: "Locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxUnits",
                table: "Locations",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxVolumeCubicMeters",
                table: "Locations",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxWeightKg",
                table: "Locations",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumTemperatureCelsius",
                table: "Locations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumTemperatureCelsius",
                table: "Locations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Locations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StorageProfile",
                table: "Locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Locations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Storage");

            migrationBuilder.Sql(
                "UPDATE \"Locations\" SET \"MaxUnits\" = NULLIF(\"Capacity\", 0);");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Locations_WarehouseId_Id",
                table: "Locations",
                columns: LocationWarehouseIdIdColumns);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_WarehouseId_Barcode",
                table: "Locations",
                columns: LocationWarehouseIdBarcodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_WarehouseId_ParentLocationId_Priority_Code",
                table: "Locations",
                columns: LocationWarehouseIdParentPriorityCodeColumns);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_WarehouseId_Type_IsActive",
                table: "Locations",
                columns: LocationWarehouseIdTypeActiveColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Locations_WarehouseId_ParentLocationId",
                table: "Locations",
                columns: LocationWarehouseIdParentColumns,
                principalTable: "Locations",
                principalColumns: LocationWarehouseIdIdColumns,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Locations_WarehouseId_ParentLocationId",
                table: "Locations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Locations_WarehouseId_Id",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_WarehouseId_Barcode",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_WarehouseId_ParentLocationId_Priority_Code",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_WarehouseId_Type_IsActive",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "AccessRestriction",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "AllowMixedItems",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "AllowMixedLots",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "ConstraintAttributesJson",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "HazardClass",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "IsCountable",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MaxLpns",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MaxPallets",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MaxUnits",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MaxVolumeCubicMeters",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MaxWeightKg",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MaximumTemperatureCelsius",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MinimumTemperatureCelsius",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "StorageProfile",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Locations");

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Locations_ParentLocationId",
                table: "Locations",
                column: "ParentLocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
