using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSerialNumberTracking : Migration
    {
        private static readonly string[] SerialIdentityIndexColumns = ["ItemId", "Number"];
        private static readonly string[] ConflictIndexColumns = ["ItemId", "Number"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SerialNumberId",
                table: "Stock",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SerialNumberId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SerialNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    CurrentWarehouseId = table.Column<int>(type: "integer", nullable: true),
                    CurrentLocationId = table.Column<int>(type: "integer", nullable: true),
                    CurrentLicensePlate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReceiptReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipmentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HasMigrationConflict = table.Column<bool>(type: "boolean", nullable: false),
                    ConflictReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastMovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SerialNumbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_Locations_CurrentLocationId",
                        column: x => x.CurrentLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_Warehouses_CurrentWarehouseId",
                        column: x => x.CurrentWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SerialNumberMigrationConflicts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SerialNumberMigrationConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SerialNumberMigrationConflicts_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_SerialNumberId",
                table: "Stock",
                column: "SerialNumberId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Stock_SerialQuantity",
                table: "Stock",
                sql: "\"SerialNumberId\" IS NULL OR (\"QuantityAvailable\" >= 0 AND \"QuantityAvailable\" <= 1 AND \"QuantityReserved\" >= 0 AND \"QuantityReserved\" <= 1)");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_SerialNumberId",
                table: "Movements",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_CurrentLocationId",
                table: "SerialNumbers",
                column: "CurrentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_CurrentWarehouseId",
                table: "SerialNumbers",
                column: "CurrentWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_HasMigrationConflict",
                table: "SerialNumbers",
                column: "HasMigrationConflict");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_ItemId_Number",
                table: "SerialNumbers",
                columns: SerialIdentityIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_LotId",
                table: "SerialNumbers",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_Status",
                table: "SerialNumbers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumberMigrationConflicts_ItemId_Number",
                table: "SerialNumberMigrationConflicts",
                columns: ConflictIndexColumns);

            migrationBuilder.Sql("""
                WITH legacy AS (
                    SELECT s."ItemId", left(upper(btrim(s."SerialNumber")), 100) AS "Number", s."LotId",
                           s."LocationId", s."QuantityAvailable", s."QuantityReserved",
                           s."CreatedAt", 'Stock' AS "SourceType", s."Id" AS "SourceId",
                           length(btrim(s."SerialNumber")) > 100 AS "WasTruncated"
                    FROM "Stock" s
                    WHERE s."SerialNumber" IS NOT NULL AND btrim(s."SerialNumber") <> ''
                    UNION ALL
                    SELECT m."ItemId", left(upper(btrim(m."SerialNumber")), 100) AS "Number", m."LotId",
                           COALESCE(m."ToLocationId", m."FromLocationId") AS "LocationId",
                           NULL::numeric, NULL::numeric, m."CreatedAt", 'Movement', m."Id",
                           length(btrim(m."SerialNumber")) > 100 AS "WasTruncated"
                    FROM "Movements" m
                    WHERE m."SerialNumber" IS NOT NULL AND btrim(m."SerialNumber") <> ''
                ), grouped AS (
                    SELECT "ItemId", "Number",
                           MIN("LotId") AS "LotId",
                           CASE WHEN COUNT(DISTINCT "LocationId") <= 1 THEN MIN("LocationId") END AS "CurrentLocationId",
                            CASE WHEN BOOL_AND(NOT "WasTruncated")
                                      AND COUNT(DISTINCT COALESCE("LotId", 0)) <= 1
                                      AND COUNT(DISTINCT "LocationId") <= 1
                                      AND COALESCE(SUM("QuantityAvailable"), 0) >= 0
                                      AND COALESCE(SUM("QuantityAvailable"), 0) <= 1
                                      AND COALESCE(SUM("QuantityReserved"), 0) >= 0
                                      AND COALESCE(SUM("QuantityReserved"), 0) <= 1
                                 THEN FALSE ELSE TRUE END AS "HasConflict",
                           MIN("CreatedAt") AS "CreatedAt",
                           MAX("CreatedAt") AS "LastMovedAt"
                    FROM legacy
                    GROUP BY "ItemId", "Number"
                )
                INSERT INTO "SerialNumbers" ("Number", "ItemId", "LotId", "CurrentWarehouseId", "CurrentLocationId", "Status", "StatusReason", "HasMigrationConflict", "ConflictReason", "LastMovedAt", "CreatedAt")
                SELECT g."Number", g."ItemId", g."LotId", l."WarehouseId", g."CurrentLocationId",
                       CASE WHEN g."HasConflict" THEN 8 ELSE 1 END,
                       CASE WHEN g."HasConflict" THEN 'migration conflict' ELSE NULL END,
                       g."HasConflict",
                       CASE WHEN g."HasConflict" THEN 'Legacy serial data contains conflicting lot, location, or quantity dimensions.' ELSE NULL END,
                       g."LastMovedAt", g."CreatedAt"
                FROM grouped g
                LEFT JOIN "Locations" l ON l."Id" = g."CurrentLocationId"
                ON CONFLICT ("ItemId", "Number") DO NOTHING;
                """);

            migrationBuilder.Sql("""
                WITH legacy AS (
                    SELECT s."ItemId", left(upper(btrim(s."SerialNumber")), 100) AS "Number", s."LotId",
                           s."LocationId", s."QuantityAvailable", s."QuantityReserved",
                           'Stock' AS "SourceType", s."Id" AS "SourceId",
                           length(btrim(s."SerialNumber")) > 100 AS "WasTruncated"
                    FROM "Stock" s
                    WHERE s."SerialNumber" IS NOT NULL AND btrim(s."SerialNumber") <> ''
                    UNION ALL
                    SELECT m."ItemId", left(upper(btrim(m."SerialNumber")), 100) AS "Number", m."LotId",
                           COALESCE(m."ToLocationId", m."FromLocationId"), NULL::numeric, NULL::numeric,
                           'Movement' AS "SourceType", m."Id" AS "SourceId",
                           length(btrim(m."SerialNumber")) > 100 AS "WasTruncated"
                    FROM "Movements" m
                    WHERE m."SerialNumber" IS NOT NULL AND btrim(m."SerialNumber") <> ''
                ), grouped AS (
                    SELECT "ItemId", "Number",
                           BOOL_OR("WasTruncated") OR
                           COUNT(DISTINCT COALESCE("LotId", 0)) > 1 OR
                           COUNT(DISTINCT "LocationId") > 1 OR
                           COALESCE(SUM("QuantityAvailable"), 0) < 0 OR
                           COALESCE(SUM("QuantityAvailable"), 0) > 1 OR
                           COALESCE(SUM("QuantityReserved"), 0) < 0 OR
                           COALESCE(SUM("QuantityReserved"), 0) > 1 AS "HasConflict"
                    FROM legacy
                    GROUP BY "ItemId", "Number"
                )
                INSERT INTO "SerialNumberMigrationConflicts" ("ItemId", "Number", "SourceType", "SourceId", "Reason", "CreatedAt")
                SELECT l."ItemId", l."Number", l."SourceType", l."SourceId",
                       'Legacy serial data conflicts with one-unit identity rules.', CURRENT_TIMESTAMP
                FROM grouped g
                JOIN legacy l ON l."ItemId" = g."ItemId" AND l."Number" = g."Number"
                WHERE g."HasConflict";
                """);

            migrationBuilder.Sql("""
                UPDATE "Stock" s
                SET "SerialNumberId" = serial."Id"
                FROM "SerialNumbers" serial
                WHERE s."SerialNumber" IS NOT NULL
                  AND serial."ItemId" = s."ItemId"
                  AND serial."Number" = left(upper(btrim(s."SerialNumber")), 100)
                  AND serial."HasMigrationConflict" = FALSE;

                UPDATE "Movements" m
                SET "SerialNumberId" = serial."Id"
                FROM "SerialNumbers" serial
                WHERE m."SerialNumber" IS NOT NULL
                  AND serial."ItemId" = m."ItemId"
                  AND serial."Number" = left(upper(btrim(m."SerialNumber")), 100)
                  AND serial."HasMigrationConflict" = FALSE;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_SerialNumbers_SerialNumberId",
                table: "Movements",
                column: "SerialNumberId",
                principalTable: "SerialNumbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stock_SerialNumbers_SerialNumberId",
                table: "Stock",
                column: "SerialNumberId",
                principalTable: "SerialNumbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_SerialNumbers_SerialNumberId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Stock_SerialNumbers_SerialNumberId",
                table: "Stock");

            migrationBuilder.DropForeignKey(
                name: "FK_SerialNumberMigrationConflicts_Items_ItemId",
                table: "SerialNumberMigrationConflicts");

            migrationBuilder.DropTable(
                name: "SerialNumbers");

            migrationBuilder.DropTable(
                name: "SerialNumberMigrationConflicts");

            migrationBuilder.DropIndex(
                name: "IX_Stock_SerialNumberId",
                table: "Stock");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Stock_SerialQuantity",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Movements_SerialNumberId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "SerialNumberId",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "SerialNumberId",
                table: "Movements");
        }
    }
}
