using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReservations : Migration
    {
        private static readonly string[] LocationPrincipalColumns = ["WarehouseId", "Id"];
        private static readonly string[] ReservationStatusColumns = ["ReservationId", "Status"];
        private static readonly string[] WarehouseItemLocationColumns = ["WarehouseId", "ItemId", "LocationId"];
        private static readonly string[] ReservationEventColumns = ["ReservationId", "OccurredAtUtc", "Id"];
        private static readonly string[] ReservationDemandColumns = ["DemandType", "DemandId", "DemandLine"];
        private static readonly string[] ReservationStatusExpiryColumns = ["WarehouseId", "ItemId", "Status", "ExpiresAtUtc"];
        private static readonly string[] WarehouseLocationColumns = ["WarehouseId", "LocationId"];
        private static readonly string[] WarehouseDemandColumns = ["WarehouseId", "DemandKey"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DemandType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DemandId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DemandLine = table.Column<int>(type: "integer", nullable: true),
                    DemandKey = table.Column<string>(type: "character varying(350)", maxLength: 350, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SelectorLocationId = table.Column<int>(type: "integer", nullable: true),
                    SelectorLotId = table.Column<int>(type: "integer", nullable: true),
                    SelectorSerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SelectorSerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SelectorLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    SelectorInventoryStatusId = table.Column<int>(type: "integer", nullable: true),
                    SelectorBaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.CheckConstraint("CK_InventoryReservations_RequestedPositive", "\"RequestedQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservationAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReservationId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservationAllocations", x => x.Id);
                    table.CheckConstraint("CK_InventoryReservationAllocations_QuantitiesValid", "\"AllocatedQuantity\" > 0 AND \"ConsumedQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ConsumedQuantity\" + \"ReleasedQuantity\" <= \"AllocatedQuantity\"");
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_InventoryReservations_Reser~",
                        column: x => x.ReservationId,
                        principalTable: "InventoryReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_InventoryStatuses_Inventory~",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_Locations_WarehouseId_Locat~",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: LocationPrincipalColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationAllocations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReservationId = table.Column<int>(type: "integer", nullable: false),
                    AllocationId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservationEvents", x => x.Id);
                    table.CheckConstraint("CK_InventoryReservationEvents_QuantityNonNegative", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryReservationEvents_InventoryReservationAllocations_~",
                        column: x => x.AllocationId,
                        principalTable: "InventoryReservationAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservationEvents_InventoryReservations_Reservatio~",
                        column: x => x.ReservationId,
                        principalTable: "InventoryReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_InventoryStatusId",
                table: "InventoryReservationAllocations",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_ItemId",
                table: "InventoryReservationAllocations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_LicensePlateId",
                table: "InventoryReservationAllocations",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_LotId",
                table: "InventoryReservationAllocations",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_ReservationId_Status",
                table: "InventoryReservationAllocations",
                columns: ReservationStatusColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_SerialNumberId",
                table: "InventoryReservationAllocations",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_WarehouseId_ItemId_Location~",
                table: "InventoryReservationAllocations",
                columns: WarehouseItemLocationColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_WarehouseId_LocationId",
                table: "InventoryReservationAllocations",
                columns: WarehouseLocationColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationEvents_AllocationId",
                table: "InventoryReservationEvents",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationEvents_ReservationId_OccurredAtUtc_Id",
                table: "InventoryReservationEvents",
                columns: ReservationEventColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_DemandType_DemandId_DemandLine",
                table: "InventoryReservations",
                columns: ReservationDemandColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_ItemId",
                table: "InventoryReservations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_WarehouseId_DemandKey",
                table: "InventoryReservations",
                columns: WarehouseDemandColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_WarehouseId_ItemId_Status_ExpiresAtUtc",
                table: "InventoryReservations",
                columns: ReservationStatusExpiryColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryReservationEvents");

            migrationBuilder.DropTable(
                name: "InventoryReservationAllocations");

            migrationBuilder.DropTable(
                name: "InventoryReservations");
        }
    }
}
