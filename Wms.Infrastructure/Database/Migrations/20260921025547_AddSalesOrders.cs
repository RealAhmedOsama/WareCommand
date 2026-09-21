using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseCodeSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CustomerCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerLegalNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerLocalizedNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerContactNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerContactEmailSnapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CustomerContactPhoneSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShipToAddressId = table.Column<int>(type: "integer", nullable: true),
                    ShipToCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShipToRecipientNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToPhoneSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShipToCountryCodeSnapshot = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ShipToRegionSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipToCitySnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipToPostalCodeSnapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ShipToAddressLine1Snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToAddressLine2Snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToDeliveryInstructionsSnapshot = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedShipDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    DefaultCarrierCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DefaultCarrierServiceCodeSnapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PackagingProfileSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LabelProfileSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AllowPartialShipmentSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusBeforeHold = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    HoldReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ConfirmedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HeldByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    HeldAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemLocalizedNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerItemSkuSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OrderedUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    AllocatedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    PickedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    PackedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ShippedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CancelledBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPrecision = table.Column<int>(type: "integer", nullable: false),
                    ConversionPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConversionRuleIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PackagingCodeSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PackagingVersionSnapshot = table.Column<int>(type: "integer", nullable: true),
                    PackagingUnitsPerPackageSnapshot = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_ItemId_SalesOrderId",
                table: "SalesOrderLines",
                columns: new[] { "ItemId", "SalesOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesOrderId_LineNumber",
                table: "SalesOrderLines",
                columns: new[] { "SalesOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomerId_OrderDate",
                table: "SalesOrders",
                columns: new[] { "CustomerId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_DocumentNumber",
                table: "SalesOrders",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_WarehouseId_SourceType_ExternalReference",
                table: "SalesOrders",
                columns: new[] { "WarehouseId", "SourceType", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_WarehouseId_Status_Priority_RequestedShipDate",
                table: "SalesOrders",
                columns: new[] { "WarehouseId", "Status", "Priority", "RequestedShipDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "SalesOrders");
        }
    }
}
