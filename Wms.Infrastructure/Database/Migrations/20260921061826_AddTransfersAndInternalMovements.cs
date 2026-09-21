using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfersAndInternalMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InternalMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalMovements_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalMovements_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalMovements_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalMovements_Locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalMovements_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalMovements_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalMovements_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransferNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreationIdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CreationRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "integer", nullable: false),
                    DestinationWarehouseId = table.Column<int>(type: "integer", nullable: false),
                    TransitLocationId = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShippedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExceptionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferOrders_Locations_TransitLocationId",
                        column: x => x.TransitLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrders_Warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrders_Warehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransferOrderId = table.Column<int>(type: "integer", nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferCommands_TransferOrders_TransferOrderId",
                        column: x => x.TransferOrderId,
                        principalTable: "TransferOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransferOrderId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    SourceInventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    DestinationInventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferOrderLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrderLines_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrderLines_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrderLines_Locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrderLines_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrderLines_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOrderLines_TransferOrders_TransferOrderId",
                        column: x => x.TransferOrderId,
                        principalTable: "TransferOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "InventoryStatuses",
                columns: new[] { "Id", "Code", "CreatedAt", "DefaultLocationType", "ForceForLocationType", "IsActive", "IsAllocatable", "IsAvailable", "IsCountable", "IsPickable", "IsShippable", "IsSystem", "LocalizedName", "Name", "UpdatedAt", "WarehouseId" },
                values: new object[] { 10, "IN_TRANSIT", new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 15, true, true, false, false, true, false, false, true, "قيد النقل", "In Transit", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_DestinationLocationId",
                table: "InternalMovements",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_ItemId",
                table: "InternalMovements",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_LicensePlateId",
                table: "InternalMovements",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_LotId",
                table: "InternalMovements",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_SerialNumberId",
                table: "InternalMovements",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_SourceLocationId",
                table: "InternalMovements",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_WarehouseId_IdempotencyKey",
                table: "InternalMovements",
                columns: new[] { "WarehouseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_WarehouseId_Status",
                table: "InternalMovements",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferCommands_TransferOrderId_Operation_IdempotencyKey",
                table: "TransferCommands",
                columns: new[] { "TransferOrderId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_DestinationLocationId",
                table: "TransferOrderLines",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_ItemId_SourceLocationId_LotId_SerialNumb~",
                table: "TransferOrderLines",
                columns: new[] { "ItemId", "SourceLocationId", "LotId", "SerialNumberId", "LicensePlateId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_LicensePlateId",
                table: "TransferOrderLines",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_LotId",
                table: "TransferOrderLines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_SerialNumberId",
                table: "TransferOrderLines",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_SourceLocationId",
                table: "TransferOrderLines",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_TransferOrderId_Sequence",
                table: "TransferOrderLines",
                columns: new[] { "TransferOrderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_CreationIdempotencyKey",
                table: "TransferOrders",
                column: "CreationIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_DestinationWarehouseId_Status",
                table: "TransferOrders",
                columns: new[] { "DestinationWarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_SourceWarehouseId_Status",
                table: "TransferOrders",
                columns: new[] { "SourceWarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_SourceWarehouseId_TransferNumber",
                table: "TransferOrders",
                columns: new[] { "SourceWarehouseId", "TransferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_TransitLocationId",
                table: "TransferOrders",
                column: "TransitLocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InternalMovements");

            migrationBuilder.DropTable(
                name: "TransferCommands");

            migrationBuilder.DropTable(
                name: "TransferOrderLines");

            migrationBuilder.DropTable(
                name: "TransferOrders");

            migrationBuilder.DeleteData(
                table: "InventoryStatuses",
                keyColumn: "Id",
                keyValue: 10);
        }
    }
}
