using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusI~",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentPackageContents_ShipmentPackageId_SalesOrderLineId_~",
                table: "ShipmentPackageContents");

            migrationBuilder.DropIndex(
                name: "IX_LicensePlateContents_LicensePlateId_ItemId_LotId_SerialNumb~",
                table: "LicensePlateContents");

            migrationBuilder.DropIndex(
                name: "IX_InventoryBalances_WarehouseId_LocationId_ItemId_LotId_Seria~",
                table: "InventoryBalances");

            migrationBuilder.DropIndex(
                name: "IX_CycleCountLines_WarehouseId_LocationId_ItemId_LotId_SerialN~",
                table: "CycleCountLines");

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "WarehouseWorkLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "WarehouseWorkLines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "WarehouseWorkLines",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "TransferOrderLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "TransferOrderLines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "TransferOrderLines",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "Stock",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "Stock",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "Stock",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "ShipmentPackageContents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "ShipmentPackageContents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "ShipmentPackageContents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "ReceiptLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "ReceiptLines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "ReceiptLines",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "Movements",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "Movements",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "LicensePlateContents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "LicensePlateContents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "LicensePlateContents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "InventoryTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "InventoryTransactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "InventoryTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SelectorInventoryOwnerId",
                table: "InventoryReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectorOwnerCodeSnapshot",
                table: "InventoryReservations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectorOwnerKind",
                table: "InventoryReservations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "InventoryReservationAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "InventoryReservationAllocations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "InventoryReservationAllocations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "InventoryBalances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "InventoryBalances",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "InventoryBalances",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "InternalMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "InternalMovements",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "InternalMovements",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryOwnerId",
                table: "CycleCountLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCodeSnapshot",
                table: "CycleCountLines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "COMPANY");

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "CycleCountLines",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "InventoryOwners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    ExternalOwnerReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryOwners", x => x.Id);
                    table.CheckConstraint("CK_InventoryOwners_LinkedOwner", "(\"Kind\" = 2 AND \"SupplierId\" IS NOT NULL AND \"CustomerId\" IS NULL AND \"ExternalOwnerReference\" IS NULL) OR (\"Kind\" = 3 AND \"SupplierId\" IS NULL AND \"CustomerId\" IS NOT NULL AND \"ExternalOwnerReference\" IS NULL) OR (\"Kind\" = 4 AND \"SupplierId\" IS NULL AND \"CustomerId\" IS NULL AND \"ExternalOwnerReference\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InventoryOwners_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwners_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryOwnershipTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransferNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    SourceOwnerKind = table.Column<int>(type: "integer", nullable: false),
                    SourceInventoryOwnerId = table.Column<int>(type: "integer", nullable: true),
                    SourceOwnerCodeSnapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DestinationOwnerKind = table.Column<int>(type: "integer", nullable: false),
                    DestinationInventoryOwnerId = table.Column<int>(type: "integer", nullable: true),
                    DestinationOwnerCodeSnapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExceptionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryOwnershipTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_InventoryOwners_DestinationInve~",
                        column: x => x.DestinationInventoryOwnerId,
                        principalTable: "InventoryOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_InventoryOwners_SourceInventory~",
                        column: x => x.SourceInventoryOwnerId,
                        principalTable: "InventoryOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_InventoryStatuses_InventoryStat~",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryOwnershipTransfers_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_InventoryOwnerId",
                table: "WarehouseWorkLines",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_OwnerKind_InventoryOwnerId",
                table: "WarehouseWorkLines",
                columns: new[] { "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_InventoryOwnerId",
                table: "TransferOrderLines",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderLines_OwnerKind_InventoryOwnerId",
                table: "TransferOrderLines",
                columns: new[] { "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_InventoryOwnerId",
                table: "Stock",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusI~",
                table: "Stock",
                columns: new[] { "ItemId", "LocationId", "LotId", "SerialNumber", "InventoryStatusId", "LicensePlateId", "OwnerKind", "InventoryOwnerId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_InventoryOwnerId",
                table: "ShipmentPackageContents",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_ShipmentPackageId_SalesOrderLineId_~",
                table: "ShipmentPackageContents",
                columns: new[] { "ShipmentPackageId", "SalesOrderLineId", "ItemId", "LotId", "SerialNumberId", "SourceLicensePlateId", "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_InventoryOwnerId",
                table: "ReceiptLines",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_OwnerKind_InventoryOwnerId",
                table: "ReceiptLines",
                columns: new[] { "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_InventoryOwnerId",
                table: "Movements",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_InventoryOwnerId",
                table: "LicensePlateContents",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_LicensePlateId_ItemId_LotId_SerialNumb~",
                table: "LicensePlateContents",
                columns: new[] { "LicensePlateId", "ItemId", "LotId", "SerialNumberId", "InventoryStatusId", "ItemPackagingId", "OwnerKind", "InventoryOwnerId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_InventoryOwnerId",
                table: "InventoryTransactions",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservationAllocations_InventoryOwnerId",
                table: "InventoryReservationAllocations",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_InventoryOwnerId",
                table: "InventoryBalances",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_WarehouseId_LocationId_ItemId_LotId_Seria~",
                table: "InventoryBalances",
                columns: new[] { "WarehouseId", "LocationId", "ItemId", "LotId", "SerialNumberId", "SerialNumber", "LicensePlateId", "InventoryStatusId", "OwnerKind", "InventoryOwnerId", "BaseUnitOfMeasure" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_InventoryOwnerId",
                table: "InternalMovements",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalMovements_OwnerKind_InventoryOwnerId",
                table: "InternalMovements",
                columns: new[] { "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_InventoryOwnerId",
                table: "CycleCountLines",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_OwnerKind_InventoryOwnerId",
                table: "CycleCountLines",
                columns: new[] { "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_WarehouseId_LocationId_ItemId_LotId_SerialN~",
                table: "CycleCountLines",
                columns: new[] { "WarehouseId", "LocationId", "ItemId", "LotId", "SerialNumberId", "LicensePlateId", "InventoryStatusId", "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwners_CustomerId",
                table: "InventoryOwners",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwners_Kind_SupplierId_CustomerId_ExternalOwnerRef~",
                table: "InventoryOwners",
                columns: new[] { "Kind", "SupplierId", "CustomerId", "ExternalOwnerReference" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwners_OwnerCode",
                table: "InventoryOwners",
                column: "OwnerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwners_SupplierId",
                table: "InventoryOwners",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_DestinationInventoryOwnerId",
                table: "InventoryOwnershipTransfers",
                column: "DestinationInventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_IdempotencyKey",
                table: "InventoryOwnershipTransfers",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_InventoryStatusId",
                table: "InventoryOwnershipTransfers",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_ItemId",
                table: "InventoryOwnershipTransfers",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_LicensePlateId",
                table: "InventoryOwnershipTransfers",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_LocationId",
                table: "InventoryOwnershipTransfers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_LotId",
                table: "InventoryOwnershipTransfers",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_SerialNumberId",
                table: "InventoryOwnershipTransfers",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_SourceInventoryOwnerId",
                table: "InventoryOwnershipTransfers",
                column: "SourceInventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_WarehouseId_ItemId_Status",
                table: "InventoryOwnershipTransfers",
                columns: new[] { "WarehouseId", "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOwnershipTransfers_WarehouseId_TransferNumber",
                table: "InventoryOwnershipTransfers",
                columns: new[] { "WarehouseId", "TransferNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CycleCountLines_InventoryOwners_InventoryOwnerId",
                table: "CycleCountLines",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InternalMovements_InventoryOwners_InventoryOwnerId",
                table: "InternalMovements",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryBalances_InventoryOwners_InventoryOwnerId",
                table: "InventoryBalances",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReservationAllocations_InventoryOwners_InventoryOw~",
                table: "InventoryReservationAllocations",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryOwners_InventoryOwnerId",
                table: "InventoryTransactions",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LicensePlateContents_InventoryOwners_InventoryOwnerId",
                table: "LicensePlateContents",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_InventoryOwners_InventoryOwnerId",
                table: "Movements",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptLines_InventoryOwners_InventoryOwnerId",
                table: "ReceiptLines",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentPackageContents_InventoryOwners_InventoryOwnerId",
                table: "ShipmentPackageContents",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stock_InventoryOwners_InventoryOwnerId",
                table: "Stock",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TransferOrderLines_InventoryOwners_InventoryOwnerId",
                table: "TransferOrderLines",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WarehouseWorkLines_InventoryOwners_InventoryOwnerId",
                table: "WarehouseWorkLines",
                column: "InventoryOwnerId",
                principalTable: "InventoryOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CycleCountLines_InventoryOwners_InventoryOwnerId",
                table: "CycleCountLines");

            migrationBuilder.DropForeignKey(
                name: "FK_InternalMovements_InventoryOwners_InventoryOwnerId",
                table: "InternalMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryBalances_InventoryOwners_InventoryOwnerId",
                table: "InventoryBalances");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryReservationAllocations_InventoryOwners_InventoryOw~",
                table: "InventoryReservationAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryOwners_InventoryOwnerId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_LicensePlateContents_InventoryOwners_InventoryOwnerId",
                table: "LicensePlateContents");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_InventoryOwners_InventoryOwnerId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptLines_InventoryOwners_InventoryOwnerId",
                table: "ReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentPackageContents_InventoryOwners_InventoryOwnerId",
                table: "ShipmentPackageContents");

            migrationBuilder.DropForeignKey(
                name: "FK_Stock_InventoryOwners_InventoryOwnerId",
                table: "Stock");

            migrationBuilder.DropForeignKey(
                name: "FK_TransferOrderLines_InventoryOwners_InventoryOwnerId",
                table: "TransferOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_WarehouseWorkLines_InventoryOwners_InventoryOwnerId",
                table: "WarehouseWorkLines");

            migrationBuilder.DropTable(
                name: "InventoryOwnershipTransfers");

            migrationBuilder.DropTable(
                name: "InventoryOwners");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseWorkLines_InventoryOwnerId",
                table: "WarehouseWorkLines");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseWorkLines_OwnerKind_InventoryOwnerId",
                table: "WarehouseWorkLines");

            migrationBuilder.DropIndex(
                name: "IX_TransferOrderLines_InventoryOwnerId",
                table: "TransferOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_TransferOrderLines_OwnerKind_InventoryOwnerId",
                table: "TransferOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_Stock_InventoryOwnerId",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusI~",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentPackageContents_InventoryOwnerId",
                table: "ShipmentPackageContents");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentPackageContents_ShipmentPackageId_SalesOrderLineId_~",
                table: "ShipmentPackageContents");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptLines_InventoryOwnerId",
                table: "ReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptLines_OwnerKind_InventoryOwnerId",
                table: "ReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_Movements_InventoryOwnerId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_LicensePlateContents_InventoryOwnerId",
                table: "LicensePlateContents");

            migrationBuilder.DropIndex(
                name: "IX_LicensePlateContents_LicensePlateId_ItemId_LotId_SerialNumb~",
                table: "LicensePlateContents");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_InventoryOwnerId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryReservationAllocations_InventoryOwnerId",
                table: "InventoryReservationAllocations");

            migrationBuilder.DropIndex(
                name: "IX_InventoryBalances_InventoryOwnerId",
                table: "InventoryBalances");

            migrationBuilder.DropIndex(
                name: "IX_InventoryBalances_WarehouseId_LocationId_ItemId_LotId_Seria~",
                table: "InventoryBalances");

            migrationBuilder.DropIndex(
                name: "IX_InternalMovements_InventoryOwnerId",
                table: "InternalMovements");

            migrationBuilder.DropIndex(
                name: "IX_InternalMovements_OwnerKind_InventoryOwnerId",
                table: "InternalMovements");

            migrationBuilder.DropIndex(
                name: "IX_CycleCountLines_InventoryOwnerId",
                table: "CycleCountLines");

            migrationBuilder.DropIndex(
                name: "IX_CycleCountLines_OwnerKind_InventoryOwnerId",
                table: "CycleCountLines");

            migrationBuilder.DropIndex(
                name: "IX_CycleCountLines_WarehouseId_LocationId_ItemId_LotId_SerialN~",
                table: "CycleCountLines");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "WarehouseWorkLines");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "WarehouseWorkLines");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "WarehouseWorkLines");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "TransferOrderLines");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "TransferOrderLines");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "TransferOrderLines");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "ShipmentPackageContents");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "ShipmentPackageContents");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "ShipmentPackageContents");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "ReceiptLines");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "ReceiptLines");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "ReceiptLines");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "LicensePlateContents");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "LicensePlateContents");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "LicensePlateContents");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "SelectorInventoryOwnerId",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "SelectorOwnerCodeSnapshot",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "SelectorOwnerKind",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "InventoryReservationAllocations");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "InventoryReservationAllocations");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "InventoryReservationAllocations");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "InternalMovements");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "InternalMovements");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "InternalMovements");

            migrationBuilder.DropColumn(
                name: "InventoryOwnerId",
                table: "CycleCountLines");

            migrationBuilder.DropColumn(
                name: "OwnerCodeSnapshot",
                table: "CycleCountLines");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "CycleCountLines");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber_InventoryStatusI~",
                table: "Stock",
                columns: new[] { "ItemId", "LocationId", "LotId", "SerialNumber", "InventoryStatusId", "LicensePlateId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_ShipmentPackageId_SalesOrderLineId_~",
                table: "ShipmentPackageContents",
                columns: new[] { "ShipmentPackageId", "SalesOrderLineId", "ItemId", "LotId", "SerialNumberId", "SourceLicensePlateId" });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateContents_LicensePlateId_ItemId_LotId_SerialNumb~",
                table: "LicensePlateContents",
                columns: new[] { "LicensePlateId", "ItemId", "LotId", "SerialNumberId", "InventoryStatusId", "ItemPackagingId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_WarehouseId_LocationId_ItemId_LotId_Seria~",
                table: "InventoryBalances",
                columns: new[] { "WarehouseId", "LocationId", "ItemId", "LotId", "SerialNumberId", "SerialNumber", "LicensePlateId", "InventoryStatusId", "BaseUnitOfMeasure" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_WarehouseId_LocationId_ItemId_LotId_SerialN~",
                table: "CycleCountLines",
                columns: new[] { "WarehouseId", "LocationId", "ItemId", "LotId", "SerialNumberId", "LicensePlateId", "InventoryStatusId" });
        }
    }
}
#pragma warning restore CA1861
