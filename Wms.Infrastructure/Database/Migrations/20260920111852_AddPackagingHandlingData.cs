using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPackagingHandlingData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackagingCode",
                table: "Movements",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingGrossWeightKg",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingHeightCm",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackagingId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingLengthCm",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingLocalizedName",
                table: "Movements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingName",
                table: "Movements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingPartialPackagePolicy",
                table: "Movements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingType",
                table: "Movements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingUnitOfMeasure",
                table: "Movements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingUnitsPerPackage",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackagingVersion",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingVolumeCubicMeters",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingWidthCm",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "WidthCm",
                table: "ItemPackagings",
                type: "numeric(28,12)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitsPerPackage",
                table: "ItemPackagings",
                type: "numeric(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LengthCm",
                table: "ItemPackagings",
                type: "numeric(28,12)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "HeightCm",
                table: "ItemPackagings",
                type: "numeric(28,12)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "GrossWeightKg",
                table: "ItemPackagings",
                type: "numeric(28,12)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gtin",
                table: "ItemPackagings",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ItemPackagings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultPicking",
                table: "ItemPackagings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultReceiving",
                table: "ItemPackagings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultShipping",
                table: "ItemPackagings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultStorage",
                table: "ItemPackagings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LocalizedName",
                table: "ItemPackagings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ItemPackagings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParentPackagingCode",
                table: "ItemPackagings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartialPackagePolicy",
                table: "ItemPackagings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Reject");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "ItemPackagings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ItemPackagings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeCubicMeters",
                table: "ItemPackagings",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movements_PackagingCode",
                table: "Movements",
                column: "PackagingCode");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPackagings_Gtin",
                table: "ItemPackagings",
                column: "Gtin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPackagings_ItemId_IsActive",
                table: "ItemPackagings",
                columns: ["ItemId", "IsActive"]);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPackagings_ItemId_ParentPackagingCode",
                table: "ItemPackagings",
                columns: ["ItemId", "ParentPackagingCode"]);

            migrationBuilder.Sql("""
                UPDATE "ItemPackagings"
                SET "Name" = "Code",
                    "LocalizedName" = "Code",
                    "IsDefaultStorage" = "IsDefault",
                    "VolumeCubicMeters" = CASE
                        WHEN "LengthCm" IS NOT NULL AND "WidthCm" IS NOT NULL AND "HeightCm" IS NOT NULL
                        THEN ("LengthCm" * "WidthCm" * "HeightCm") / 1000000
                        ELSE NULL
                    END
                WHERE "Name" = '' OR "LocalizedName" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Movements_PackagingCode",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_ItemPackagings_Gtin",
                table: "ItemPackagings");

            migrationBuilder.DropIndex(
                name: "IX_ItemPackagings_ItemId_IsActive",
                table: "ItemPackagings");

            migrationBuilder.DropIndex(
                name: "IX_ItemPackagings_ItemId_ParentPackagingCode",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "PackagingCode",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingGrossWeightKg",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingHeightCm",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingLengthCm",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingLocalizedName",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingName",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingPartialPackagePolicy",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingType",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingUnitOfMeasure",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingUnitsPerPackage",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingVersion",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingVolumeCubicMeters",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PackagingWidthCm",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "Gtin",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "IsDefaultPicking",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "IsDefaultReceiving",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "IsDefaultShipping",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "IsDefaultStorage",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "LocalizedName",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "ParentPackagingCode",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "PartialPackagePolicy",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ItemPackagings");

            migrationBuilder.DropColumn(
                name: "VolumeCubicMeters",
                table: "ItemPackagings");

            migrationBuilder.AlterColumn<decimal>(
                name: "WidthCm",
                table: "ItemPackagings",
                type: "numeric(18,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitsPerPackage",
                table: "ItemPackagings",
                type: "numeric(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LengthCm",
                table: "ItemPackagings",
                type: "numeric(18,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "HeightCm",
                table: "ItemPackagings",
                type: "numeric(18,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "GrossWeightKg",
                table: "ItemPackagings",
                type: "numeric(18,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldNullable: true);
        }
    }
}
