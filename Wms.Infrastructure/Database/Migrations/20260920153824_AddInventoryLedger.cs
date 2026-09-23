using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLedger : Migration
    {
        private static readonly string[] LocationKeyColumns = ["WarehouseId", "Id"];
        private static readonly string[] WarehouseItemColumns = ["WarehouseId", "ItemId"];
        private static readonly string[] WarehouseLocationColumns = ["WarehouseId", "LocationId"];
        private static readonly string[] BalanceIdentityColumns =
        [
            "WarehouseId", "LocationId", "ItemId", "LotId", "SerialNumberId",
            "SerialNumber", "LicensePlateId", "InventoryStatusId", "BaseUnitOfMeasure"
        ];
        private static readonly string[] IdempotencyColumns = ["IdempotencyKey", "EntrySequence"];
        private static readonly string[] ReferenceColumns = ["ReferenceType", "ReferenceId"];
        private static readonly string[] WarehouseItemOccurredColumns =
            ["WarehouseId", "ItemId", "OccurredAtUtc"];
        private static readonly string[] WarehouseLocationItemOccurredColumns =
            ["WarehouseId", "LocationId", "ItemId", "OccurredAtUtc"];
        private static readonly string[] WarehouseGroupSequenceColumns =
            ["WarehouseId", "TransactionGroupId", "EntrySequence"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OnHandQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalances", x => x.Id);
                    table.CheckConstraint("CK_InventoryBalances_ReservedNonNegative", "\"ReservedQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryBalances_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Locations_WarehouseId_LocationId",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: LocationKeyColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReservedQuantityDelta = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReservedQuantityBefore = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReservedQuantityAfter = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReferenceLine = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    TransactionGroupId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntrySequence = table.Column<int>(type: "integer", nullable: false),
                    MovementId = table.Column<int>(type: "integer", nullable: true),
                    ReversalOfTransactionId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_InventoryTransactions_ReversalOfTrans~",
                        column: x => x.ReversalOfTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Locations_WarehouseId_LocationId",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: LocationKeyColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The cutover is deliberately a balance snapshot. Existing
            // Movements remain the historical operational record; all changes
            // after this migration append InventoryTransactions. The opening
            // rows make the new ledger reconcile exactly to the pre-existing
            // Stock projection, including reserved quantity.
            migrationBuilder.Sql(
                """
                INSERT INTO "InventoryBalances"
                (
                    "WarehouseId", "LocationId", "ItemId", "LotId",
                    "SerialNumberId", "SerialNumber", "LicensePlateId",
                    "InventoryStatusId", "BaseUnitOfMeasure",
                    "OnHandQuantity", "ReservedQuantity", "Revision",
                    "CreatedAt", "UpdatedAt"
                )
                SELECT
                    l."WarehouseId", s."LocationId", s."ItemId", s."LotId",
                    s."SerialNumberId", s."SerialNumber", s."LicensePlateId",
                    s."InventoryStatusId", i."UnitOfMeasure",
                    s."QuantityAvailable", s."QuantityReserved", 0,
                    s."CreatedAt", s."UpdatedAt"
                FROM "Stock" AS s
                INNER JOIN "Locations" AS l ON l."Id" = s."LocationId"
                INNER JOIN "Items" AS i ON i."Id" = s."ItemId";
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "InventoryTransactions"
                (
                    "WarehouseId", "LocationId", "ItemId", "LotId",
                    "SerialNumberId", "SerialNumber", "LicensePlateId",
                    "InventoryStatusId", "BaseUnitOfMeasure", "Type",
                    "QuantityDelta", "QuantityBefore", "QuantityAfter",
                    "ReservedQuantityDelta", "ReservedQuantityBefore",
                    "ReservedQuantityAfter", "ReferenceType", "ReferenceId",
                    "ReferenceLine", "Reason", "ActorUserId", "OccurredAtUtc",
                    "CorrelationId", "IdempotencyKey", "TransactionGroupId",
                    "EntrySequence", "MovementId", "ReversalOfTransactionId",
                    "CreatedAt", "UpdatedAt"
                )
                SELECT
                    l."WarehouseId", s."LocationId", s."ItemId", s."LotId",
                    s."SerialNumberId", s."SerialNumber", s."LicensePlateId",
                    s."InventoryStatusId", i."UnitOfMeasure", 1,
                    s."QuantityAvailable", 0, s."QuantityAvailable",
                    s."QuantityReserved", 0, s."QuantityReserved",
                    'Stock', CAST(s."Id" AS TEXT), NULL,
                    'Legacy stock balance carried into the inventory ledger',
                    'system.migration', s."CreatedAt",
                    ('legacy:stock:' || CAST(s."Id" AS TEXT)),
                    ('legacy:stock:' || CAST(s."Id" AS TEXT)),
                    ('legacy:stock:' || CAST(s."Id" AS TEXT)), 1, NULL, NULL,
                    s."CreatedAt", NULL
                FROM "Stock" AS s
                INNER JOIN "Locations" AS l ON l."Id" = s."LocationId"
                INNER JOIN "Items" AS i ON i."Id" = s."ItemId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_InventoryStatusId",
                table: "InventoryBalances",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_ItemId",
                table: "InventoryBalances",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_LicensePlateId",
                table: "InventoryBalances",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_LotId",
                table: "InventoryBalances",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_SerialNumberId",
                table: "InventoryBalances",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_WarehouseId_ItemId",
                table: "InventoryBalances",
                columns: WarehouseItemColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_WarehouseId_LocationId",
                table: "InventoryBalances",
                columns: WarehouseLocationColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_WarehouseId_LocationId_ItemId_LotId_Seria~",
                table: "InventoryBalances",
                columns: BalanceIdentityColumns,
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_IdempotencyKey_EntrySequence",
                table: "InventoryTransactions",
                columns: IdempotencyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_InventoryStatusId",
                table: "InventoryTransactions",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ItemId",
                table: "InventoryTransactions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LicensePlateId",
                table: "InventoryTransactions",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LotId",
                table: "InventoryTransactions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_MovementId",
                table: "InventoryTransactions",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ReferenceType_ReferenceId",
                table: "InventoryTransactions",
                columns: ReferenceColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ReversalOfTransactionId",
                table: "InventoryTransactions",
                column: "ReversalOfTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_SerialNumberId",
                table: "InventoryTransactions",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId_ItemId_OccurredAtUtc",
                table: "InventoryTransactions",
                columns: WarehouseItemOccurredColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId_LocationId_ItemId_Occurre~",
                table: "InventoryTransactions",
                columns: WarehouseLocationItemOccurredColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId_TransactionGroupId_EntryS~",
                table: "InventoryTransactions",
                columns: WarehouseGroupSequenceColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");
        }
    }
}
