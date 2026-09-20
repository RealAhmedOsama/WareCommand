using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundReceipts : Migration
    {
        private static readonly string[] ReceiptLineLinksReceiptLineIdTypeReferenceColumns = ["ReceiptLineId", "Type", "Reference"];
        private static readonly string[] ReceiptLineMovementsReceiptLineIdMovementIdColumns = ["ReceiptLineId", "MovementId"];
        private static readonly string[] ReceiptLinesReceiptIdLineNumberColumns = ["ReceiptId", "LineNumber"];
        private static readonly string[] ReceiptsSourceTypeExternalReferenceColumns = ["SourceType", "ExternalReference"];
        private static readonly string[] ReceiptsWarehouseIdStatusColumns = ["WarehouseId", "Status"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiptId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptLineId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptMovementKind",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelatedMovementId",
                table: "Movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseCodeSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    SupplierCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: true),
                    AdvanceShippingNoticeId = table.Column<int>(type: "integer", nullable: true),
                    DockLocationId = table.Column<int>(type: "integer", nullable: true),
                    ReceivingLocationId = table.Column<int>(type: "integer", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SessionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrectedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrectedByReceiptId = table.Column<int>(type: "integer", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipts_AdvanceShippingNotices_AdvanceShippingNoticeId",
                        column: x => x.AdvanceShippingNoticeId,
                        principalTable: "AdvanceShippingNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Locations_DockLocationId",
                        column: x => x.DockLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Locations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Receipts_CorrectedByReceiptId",
                        column: x => x.CorrectedByReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnteredUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EnteredQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    AcceptedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    RejectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    DamagedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    QuarantinedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPrecision = table.Column<int>(type: "integer", nullable: false),
                    ConversionRoundingMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConversionRoundingDelta = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConversionPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConversionRuleIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PackagingId = table.Column<int>(type: "integer", nullable: true),
                    PackagingVersion = table.Column<int>(type: "integer", nullable: true),
                    PackagingCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PackagingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PackagingLocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PackagingType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PackagingUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PackagingUnitsPerPackage = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PackagingPartialPackagePolicy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PackagingGrossWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PackagingLengthCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PackagingWidthCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PackagingHeightCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PackagingVolumeCubicMeters = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseOrderLineId = table.Column<int>(type: "integer", nullable: true),
                    AdvanceShippingNoticeId = table.Column<int>(type: "integer", nullable: true),
                    AdvanceShippingNoticeLineId = table.Column<int>(type: "integer", nullable: true),
                    ReceivingLocationId = table.Column<int>(type: "integer", nullable: true),
                    LotNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDateSnapshot = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SerialNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    LicensePlateNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateIsSscc = table.Column<bool>(type: "boolean", nullable: false),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    InventoryStatusCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InventoryStatusNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_AdvanceShippingNoticeLines_AdvanceShippingNoti~",
                        column: x => x.AdvanceShippingNoticeLineId,
                        principalTable: "AdvanceShippingNoticeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_AdvanceShippingNotices_AdvanceShippingNoticeId",
                        column: x => x.AdvanceShippingNoticeId,
                        principalTable: "AdvanceShippingNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_Locations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptLineLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptLineId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptLineLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptLineLinks_ReceiptLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptLineMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptLineId = table.Column<int>(type: "integer", nullable: false),
                    MovementId = table.Column<int>(type: "integer", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    AcceptedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    RejectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    DamagedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    QuarantinedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RelatedMovementId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptLineMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptLineMovements_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLineMovements_Movements_RelatedMovementId",
                        column: x => x.RelatedMovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLineMovements_ReceiptLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ReceiptId",
                table: "Movements",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ReceiptLineId",
                table: "Movements",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_RelatedMovementId",
                table: "Movements",
                column: "RelatedMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLineLinks_ReceiptLineId_Type_Reference",
                table: "ReceiptLineLinks",
                columns: ReceiptLineLinksReceiptLineIdTypeReferenceColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLineMovements_MovementId",
                table: "ReceiptLineMovements",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLineMovements_ReceiptLineId_MovementId",
                table: "ReceiptLineMovements",
                columns: ReceiptLineMovementsReceiptLineIdMovementIdColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLineMovements_RelatedMovementId",
                table: "ReceiptLineMovements",
                column: "RelatedMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_AdvanceShippingNoticeId",
                table: "ReceiptLines",
                column: "AdvanceShippingNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_AdvanceShippingNoticeLineId",
                table: "ReceiptLines",
                column: "AdvanceShippingNoticeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_InventoryStatusId",
                table: "ReceiptLines",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_ItemId",
                table: "ReceiptLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_LicensePlateId",
                table: "ReceiptLines",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_PurchaseOrderId",
                table: "ReceiptLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_PurchaseOrderLineId",
                table: "ReceiptLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_ReceiptId_LineNumber",
                table: "ReceiptLines",
                columns: ReceiptLinesReceiptIdLineNumberColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_ReceivingLocationId",
                table: "ReceiptLines",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_AdvanceShippingNoticeId",
                table: "Receipts",
                column: "AdvanceShippingNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CorrectedByReceiptId",
                table: "Receipts",
                column: "CorrectedByReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_DockLocationId",
                table: "Receipts",
                column: "DockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_DocumentNumber",
                table: "Receipts",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_PurchaseOrderId",
                table: "Receipts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ReceivedAtUtc",
                table: "Receipts",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ReceivingLocationId",
                table: "Receipts",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_SourceType_ExternalReference",
                table: "Receipts",
                columns: ReceiptsSourceTypeExternalReferenceColumns);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_SupplierId",
                table: "Receipts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_WarehouseId_Status",
                table: "Receipts",
                columns: ReceiptsWarehouseIdStatusColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_Movements_RelatedMovementId",
                table: "Movements",
                column: "RelatedMovementId",
                principalTable: "Movements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_ReceiptLines_ReceiptLineId",
                table: "Movements",
                column: "ReceiptLineId",
                principalTable: "ReceiptLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_Receipts_ReceiptId",
                table: "Movements",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_Movements_RelatedMovementId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_ReceiptLines_ReceiptLineId",
                table: "Movements");

            migrationBuilder.DropForeignKey(
                name: "FK_Movements_Receipts_ReceiptId",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "ReceiptLineLinks");

            migrationBuilder.DropTable(
                name: "ReceiptLineMovements");

            migrationBuilder.DropTable(
                name: "ReceiptLines");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_Movements_ReceiptId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_ReceiptLineId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_RelatedMovementId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ReceiptId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ReceiptLineId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "ReceiptMovementKind",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "RelatedMovementId",
                table: "Movements");
        }
    }
}
