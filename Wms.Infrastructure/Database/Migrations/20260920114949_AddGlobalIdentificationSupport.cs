using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalIdentificationSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsIdentifiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OriginalValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Symbology = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OwnerKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Alias = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsIdentifiers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsIdentifiers_IsActive_ValidFromUtc_ValidToUtc",
                table: "WmsIdentifiers",
                columns: ["IsActive", "ValidFromUtc", "ValidToUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_WmsIdentifiers_NormalizedValue",
                table: "WmsIdentifiers",
                column: "NormalizedValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsIdentifiers_OwnerKey_Kind_IsActive",
                table: "WmsIdentifiers",
                columns: ["OwnerKey", "Kind", "IsActive"]);

            // Keep the legacy columns during rollout, but make their current
            // values resolvable through the global registry immediately. The
            // conflict-safe insert intentionally preserves the first legacy
            // owner so an old database with duplicate location barcodes does
            // not fail the schema migration; the resolver still reports the
            // remaining legacy ambiguity until an administrator corrects it.
            migrationBuilder.Sql("""
                INSERT INTO "WmsIdentifiers"
                    ("OriginalValue", "NormalizedValue", "Kind", "Symbology", "OwnerKey", "IsActive", "ValidFromUtc", "CreatedAt", "UpdatedAt")
                SELECT b."Barcode", b."Barcode", 'Item', 'Internal', 'ITEM:' || i."Sku", i."IsActive",
                       TIMESTAMP WITH TIME ZONE '1970-01-01 00:00:00+00', i."CreatedAt"::timestamptz,
                       i."UpdatedAt"::timestamptz
                FROM "ItemBarcodes" b
                INNER JOIN "Items" i ON i."Id" = b."ItemId"
                ON CONFLICT ("NormalizedValue") DO NOTHING;

                INSERT INTO "WmsIdentifiers"
                    ("OriginalValue", "NormalizedValue", "Kind", "Symbology", "OwnerKey", "IsActive", "ValidFromUtc", "CreatedAt", "UpdatedAt")
                SELECT p."Barcode", p."Barcode", 'Packaging', 'Internal', 'PACKAGING:' || i."Sku" || '|' || p."Code",
                       i."IsActive" AND p."IsActive", TIMESTAMP WITH TIME ZONE '1970-01-01 00:00:00+00',
                       p."CreatedAt"::timestamptz, p."UpdatedAt"::timestamptz
                FROM "ItemPackagings" p
                INNER JOIN "Items" i ON i."Id" = p."ItemId"
                WHERE p."Barcode" IS NOT NULL
                ON CONFLICT ("NormalizedValue") DO NOTHING;

                INSERT INTO "WmsIdentifiers"
                    ("OriginalValue", "NormalizedValue", "Kind", "Symbology", "OwnerKey", "IsActive", "ValidFromUtc", "CreatedAt", "UpdatedAt")
                SELECT p."Gtin", p."Gtin", 'Packaging', 'Gtin14', 'PACKAGING:' || i."Sku" || '|' || p."Code",
                       i."IsActive" AND p."IsActive", TIMESTAMP WITH TIME ZONE '1970-01-01 00:00:00+00',
                       p."CreatedAt"::timestamptz, p."UpdatedAt"::timestamptz
                FROM "ItemPackagings" p
                INNER JOIN "Items" i ON i."Id" = p."ItemId"
                WHERE p."Gtin" IS NOT NULL
                ON CONFLICT ("NormalizedValue") DO NOTHING;

                INSERT INTO "WmsIdentifiers"
                    ("OriginalValue", "NormalizedValue", "Kind", "Symbology", "OwnerKey", "IsActive", "ValidFromUtc", "CreatedAt", "UpdatedAt")
                SELECT l."Barcode", l."Barcode", 'Location', 'Internal', 'LOCATION:' || l."WarehouseId" || '|' || l."Code",
                       l."IsActive", TIMESTAMP WITH TIME ZONE '1970-01-01 00:00:00+00',
                       l."CreatedAt"::timestamptz, l."UpdatedAt"::timestamptz
                FROM "Locations" l
                WHERE l."Barcode" IS NOT NULL
                ON CONFLICT ("NormalizedValue") DO NOTHING;

                INSERT INTO "WmsIdentifiers"
                    ("OriginalValue", "NormalizedValue", "Kind", "Symbology", "OwnerKey", "IsActive", "ValidFromUtc", "CreatedAt", "UpdatedAt")
                SELECT lot."Number", lot."Number", 'Lot', 'Internal', 'LOT:' || lot."ItemId" || '|' || lot."Number",
                       lot."IsActive", TIMESTAMP WITH TIME ZONE '1970-01-01 00:00:00+00',
                       lot."CreatedAt"::timestamptz, lot."UpdatedAt"::timestamptz
                FROM "Lots" lot
                ON CONFLICT ("NormalizedValue") DO NOTHING;

                INSERT INTO "WmsIdentifiers"
                    ("OriginalValue", "NormalizedValue", "Kind", "Symbology", "OwnerKey", "IsActive", "ValidFromUtc", "CreatedAt", "UpdatedAt")
                SELECT DISTINCT stock."SerialNumber", stock."SerialNumber", 'Serial', 'Internal',
                       'SERIAL:' || stock."ItemId" || '|' || stock."SerialNumber", TRUE,
                       TIMESTAMP WITH TIME ZONE '1970-01-01 00:00:00+00',
                       TIMESTAMP WITH TIME ZONE '1970-01-01 00:00:00+00',
                       CAST(NULL AS timestamp with time zone)
                FROM "Stock" stock
                WHERE stock."SerialNumber" IS NOT NULL
                ON CONFLICT ("NormalizedValue") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsIdentifiers");
        }
    }
}
