using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitOfMeasureConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReserved",
                table: "Stock",
                type: "numeric(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityAvailable",
                table: "Stock",
                type: "numeric(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.AddColumn<string>(
                name: "BaseUnitOfMeasure",
                table: "Movements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactorToBase",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "ConversionPath",
                table: "Movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ConversionPrecision",
                table: "Movements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRoundingDelta",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ConversionRoundingMode",
                table: "Movements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Reject");

            migrationBuilder.AddColumn<string>(
                name: "ConversionRuleIds",
                table: "Movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "EnteredQuantity",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "EnteredUnitOfMeasure",
                table: "Movements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "BASE");

            migrationBuilder.AddColumn<bool>(
                name: "AllowFractionalQuantity",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ItemUnitConversions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    FromUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ToUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ResultPrecision = table.Column<int>(type: "integer", nullable: false),
                    RoundingMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUnitConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemUnitConversions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UnitOfMeasures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Precision = table.Column<int>(type: "integer", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitOfMeasures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversions_ItemId_FromUnitOfMeasure_ToUnitOfMeasu~1",
                table: "ItemUnitConversions",
                columns: new[] { "ItemId", "FromUnitOfMeasure", "ToUnitOfMeasure", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversions_ItemId_FromUnitOfMeasure_ToUnitOfMeasur~",
                table: "ItemUnitConversions",
                columns: new[] { "ItemId", "FromUnitOfMeasure", "ToUnitOfMeasure", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitOfMeasures_Category_IsActive",
                table: "UnitOfMeasures",
                columns: new[] { "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitOfMeasures_Code",
                table: "UnitOfMeasures",
                column: "Code",
                unique: true);

            migrationBuilder.Sql("""
                UPDATE "Movements"
                SET "EnteredQuantity" = "Quantity",
                    "BaseUnitOfMeasure" = 'BASE',
                    "ConversionPath" = 'BASE',
                    "ConversionPrecision" = 4,
                    "ConversionRuleIds" = ''
                WHERE "EnteredUnitOfMeasure" = 'BASE';
                """);

            if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""
                    INSERT INTO "UnitOfMeasures"
                        ("Code", "Category", "Precision", "Symbol", "Name", "LocalizedName", "IsActive", "CreatedAt")
                    SELECT DISTINCT source."Code", 'Count', 4, source."Code", source."Code", source."Code", 1, CURRENT_TIMESTAMP
                    FROM (
                        SELECT "UnitOfMeasure" AS "Code" FROM "Items"
                        UNION
                        SELECT "PurchaseUnit" AS "Code" FROM "Items"
                        UNION
                        SELECT "SalesUnit" AS "Code" FROM "Items"
                        UNION
                        SELECT "UnitOfMeasure" AS "Code" FROM "ItemPackagings"
                    ) AS source
                    WHERE source."Code" <> ''
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "UnitOfMeasures" existing
                          WHERE existing."Code" = source."Code");
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    INSERT INTO "UnitOfMeasures"
                        ("Code", "Category", "Precision", "Symbol", "Name", "LocalizedName", "IsActive", "CreatedAt")
                    SELECT DISTINCT source."Code", 'Count', 4, source."Code", source."Code", source."Code", TRUE, CURRENT_TIMESTAMP
                    FROM (
                        SELECT "UnitOfMeasure" AS "Code" FROM "Items"
                        UNION
                        SELECT "PurchaseUnit" AS "Code" FROM "Items"
                        UNION
                        SELECT "SalesUnit" AS "Code" FROM "Items"
                        UNION
                        SELECT "UnitOfMeasure" AS "Code" FROM "ItemPackagings"
                    ) AS source
                    WHERE source."Code" <> ''
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "UnitOfMeasures" existing
                          WHERE existing."Code" = source."Code");
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemUnitConversions");

            migrationBuilder.DropTable(
                name: "UnitOfMeasures");

            migrationBuilder.DropColumn(
                name: "BaseUnitOfMeasure",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ConversionFactorToBase",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ConversionPath",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ConversionPrecision",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ConversionRoundingDelta",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ConversionRoundingMode",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ConversionRuleIds",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "EnteredQuantity",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "EnteredUnitOfMeasure",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "AllowFractionalQuantity",
                table: "Items");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReserved",
                table: "Stock",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityAvailable",
                table: "Stock",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "Movements",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)");
        }
    }
}
