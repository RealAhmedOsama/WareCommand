using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrders : Migration
    {
        private static readonly string[] ItemPurchaseOrderColumns = ["ItemId", "PurchaseOrderId"];
        private static readonly string[] PurchaseOrderLineColumns = ["PurchaseOrderId", "LineNumber"];
        private static readonly string[] ReceiptAllocationTimestampColumns = ["PurchaseOrderLineId", "ReceivedAtUtc"];
        private static readonly string[] SupplierOrderDateColumns = ["SupplierId", "OrderDate"];
        private static readonly string[] SupplierSourceReferenceColumns = ["SupplierId", "SourceType", "ExternalReference"];
        private static readonly string[] WarehouseStatusOrderDateColumns = ["WarehouseId", "Status", "OrderDate"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderLineId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseCodeSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    SupplierCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SupplierNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrencyCodeSnapshot = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ConfirmedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrderedUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPrecision = table.Column<int>(type: "integer", nullable: false),
                    ConversionRoundingMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConversionRoundingDelta = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConversionRuleIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OverDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnderDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    SupplierItemReferenceSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    MovementId = table.Column<int>(type: "integer", nullable: false),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    EnteredQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    EnteredUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPrecision = table.Column<int>(type: "integer", nullable: false),
                    ConversionRoundingMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConversionRoundingDelta = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConversionRuleIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReceivedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderReceiptAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderReceiptAllocations_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderReceiptAllocations_PurchaseOrderLines_Purchase~",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderReceiptAllocations_PurchaseOrders_PurchaseOrde~",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_PurchaseOrderId",
                table: "Movements",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_PurchaseOrderLineId",
                table: "Movements",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ItemId_PurchaseOrderId",
                table: "PurchaseOrderLines",
                columns: ItemPurchaseOrderColumns);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId_LineNumber",
                table: "PurchaseOrderLines",
                columns: PurchaseOrderLineColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceiptAllocations_MovementId",
                table: "PurchaseOrderReceiptAllocations",
                column: "MovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceiptAllocations_PurchaseOrderId",
                table: "PurchaseOrderReceiptAllocations",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceiptAllocations_PurchaseOrderLineId",
                table: "PurchaseOrderReceiptAllocations",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceiptAllocations_PurchaseOrderLineId_Receive~",
                table: "PurchaseOrderReceiptAllocations",
                columns: ReceiptAllocationTimestampColumns);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_DocumentNumber",
                table: "PurchaseOrders",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId_OrderDate",
                table: "PurchaseOrders",
                columns: SupplierOrderDateColumns);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId_SourceType_ExternalReference",
                table: "PurchaseOrders",
                columns: SupplierSourceReferenceColumns);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_WarehouseId_Status_OrderDate",
                table: "PurchaseOrders",
                columns: WarehouseStatusOrderDateColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_PurchaseOrderLines_PurchaseOrderLineId",
                table: "Movements",
                column: "PurchaseOrderLineId",
                principalTable: "PurchaseOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_PurchaseOrders_PurchaseOrderId",
                table: "Movements",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_PurchaseOrderLines_PurchaseOrderLineId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_PurchaseOrders_PurchaseOrderId",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "PurchaseOrderReceiptAllocations");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Movements_PurchaseOrderId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_PurchaseOrderLineId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderLineId",
                table: "Movements");
        }
    }
}
