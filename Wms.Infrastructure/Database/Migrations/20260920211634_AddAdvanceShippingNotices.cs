using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvanceShippingNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NextAdvanceShippingNoticeNumber",
                table: "WarehouseNumberSequences",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AdvanceShippingNoticeId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdvanceShippingNoticeLineId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdvanceShippingNotices",
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
                    CarrierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExpectedArrivalFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedArrivalToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VehicleNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrailerNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContainerNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrackingReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourcePayload = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    DockLocationId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArrivedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ArrivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceShippingNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNotices_Locations_DockLocationId",
                        column: x => x.DockLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNotices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNotices_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvanceShippingNoticeLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvanceShippingNoticeId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnteredUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPrecision = table.Column<int>(type: "integer", nullable: false),
                    ConversionRoundingMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConversionRoundingDelta = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConversionRuleIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OverDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnderDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ItemPackagingId = table.Column<int>(type: "integer", nullable: true),
                    ItemPackagingCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ItemPackagingNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ItemPackagingUnitOfMeasureSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ItemPackagingUnitsPerPackageSnapshot = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseOrderLineId = table.Column<int>(type: "integer", nullable: true),
                    PreAdvisedLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PreAdvisedExpiryDate = table.Column<DateTime>(type: "date", nullable: true),
                    PreAdvisedSerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpectedLicensePlateNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpectedLicensePlateIsSscc = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceShippingNoticeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeLines_AdvanceShippingNotices_AdvanceSh~",
                        column: x => x.AdvanceShippingNoticeId,
                        principalTable: "AdvanceShippingNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeLines_ItemPackagings_ItemPackagingId",
                        column: x => x.ItemPackagingId,
                        principalTable: "ItemPackagings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeLines_PurchaseOrderLines_PurchaseOrder~",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvanceShippingNoticeDiscrepancies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvanceShippingNoticeId = table.Column<int>(type: "integer", nullable: false),
                    AdvanceShippingNoticeLineId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ExpectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    VarianceBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceShippingNoticeDiscrepancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeDiscrepancies_AdvanceShippingNoticeLin~",
                        column: x => x.AdvanceShippingNoticeLineId,
                        principalTable: "AdvanceShippingNoticeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeDiscrepancies_AdvanceShippingNotices_A~",
                        column: x => x.AdvanceShippingNoticeId,
                        principalTable: "AdvanceShippingNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvanceShippingNoticeReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvanceShippingNoticeId = table.Column<int>(type: "integer", nullable: false),
                    AdvanceShippingNoticeLineId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AdvanceShippingNoticeReceiptAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeReceiptAllocations_AdvanceShippingNoti~",
                        column: x => x.AdvanceShippingNoticeId,
                        principalTable: "AdvanceShippingNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeReceiptAllocations_AdvanceShippingNot~1",
                        column: x => x.AdvanceShippingNoticeLineId,
                        principalTable: "AdvanceShippingNoticeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceShippingNoticeReceiptAllocations_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_AdvanceShippingNoticeId",
                table: "Movements",
                column: "AdvanceShippingNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_AdvanceShippingNoticeLineId",
                table: "Movements",
                column: "AdvanceShippingNoticeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeDiscrepancies_AdvanceShippingNoticeId_~",
                table: "AdvanceShippingNoticeDiscrepancies",
                columns: new[] { "AdvanceShippingNoticeId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeDiscrepancies_AdvanceShippingNoticeLin~",
                table: "AdvanceShippingNoticeDiscrepancies",
                column: "AdvanceShippingNoticeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeLines_AdvanceShippingNoticeId_LineNumb~",
                table: "AdvanceShippingNoticeLines",
                columns: new[] { "AdvanceShippingNoticeId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeLines_ItemId_AdvanceShippingNoticeId",
                table: "AdvanceShippingNoticeLines",
                columns: new[] { "ItemId", "AdvanceShippingNoticeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeLines_ItemPackagingId",
                table: "AdvanceShippingNoticeLines",
                column: "ItemPackagingId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeLines_PurchaseOrderId",
                table: "AdvanceShippingNoticeLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeLines_PurchaseOrderLineId_AdvanceShipp~",
                table: "AdvanceShippingNoticeLines",
                columns: new[] { "PurchaseOrderLineId", "AdvanceShippingNoticeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeReceiptAllocations_AdvanceShippingNot~1",
                table: "AdvanceShippingNoticeReceiptAllocations",
                column: "AdvanceShippingNoticeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeReceiptAllocations_AdvanceShippingNot~2",
                table: "AdvanceShippingNoticeReceiptAllocations",
                columns: new[] { "AdvanceShippingNoticeLineId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeReceiptAllocations_AdvanceShippingNoti~",
                table: "AdvanceShippingNoticeReceiptAllocations",
                column: "AdvanceShippingNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNoticeReceiptAllocations_MovementId",
                table: "AdvanceShippingNoticeReceiptAllocations",
                column: "MovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNotices_DockLocationId",
                table: "AdvanceShippingNotices",
                column: "DockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNotices_DocumentNumber",
                table: "AdvanceShippingNotices",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNotices_SupplierId_ExpectedArrivalFromUtc",
                table: "AdvanceShippingNotices",
                columns: new[] { "SupplierId", "ExpectedArrivalFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNotices_SupplierId_SourceType_ExternalRefere~",
                table: "AdvanceShippingNotices",
                columns: new[] { "SupplierId", "SourceType", "ExternalReference" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNotices_WarehouseId_Status_ExpectedArrivalFr~",
                table: "AdvanceShippingNotices",
                columns: new[] { "WarehouseId", "Status", "ExpectedArrivalFromUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_AdvanceShippingNoticeLines_AdvanceShippingNoticeL~",
                table: "Movements",
                column: "AdvanceShippingNoticeLineId",
                principalTable: "AdvanceShippingNoticeLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_AdvanceShippingNotices_AdvanceShippingNoticeId",
                table: "Movements",
                column: "AdvanceShippingNoticeId",
                principalTable: "AdvanceShippingNotices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_AdvanceShippingNoticeLines_AdvanceShippingNoticeL~",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_AdvanceShippingNotices_AdvanceShippingNoticeId",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "AdvanceShippingNoticeDiscrepancies");

            migrationBuilder.DropTable(
                name: "AdvanceShippingNoticeReceiptAllocations");

            migrationBuilder.DropTable(
                name: "AdvanceShippingNoticeLines");

            migrationBuilder.DropTable(
                name: "AdvanceShippingNotices");

            migrationBuilder.DropIndex(
                name: "IX_Movements_AdvanceShippingNoticeId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_AdvanceShippingNoticeLineId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "NextAdvanceShippingNoticeNumber",
                table: "WarehouseNumberSequences");

            migrationBuilder.DropColumn(
                name: "AdvanceShippingNoticeId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "AdvanceShippingNoticeLineId",
                table: "Movements");
        }
    }
}
#pragma warning restore CA1861
