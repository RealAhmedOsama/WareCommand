using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddScannerReceivingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceivingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ReceivingLocationId = table.Column<int>(type: "integer", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    SessionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: true),
                    AdvanceShippingNoticeId = table.Column<int>(type: "integer", nullable: true),
                    DockLocationId = table.Column<int>(type: "integer", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SupervisorOverride = table.Column<bool>(type: "boolean", nullable: false),
                    SupervisorOverrideReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingSessions_AdvanceShippingNotices_AdvanceShippingNot~",
                        column: x => x.AdvanceShippingNoticeId,
                        principalTable: "AdvanceShippingNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessions_Locations_DockLocationId",
                        column: x => x.DockLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessions_Locations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessions_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingSessionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceivingSessionId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    PreviouslyReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    OverDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    UnderDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "integer", nullable: true),
                    AdvanceShippingNoticeLineId = table.Column<int>(type: "integer", nullable: true),
                    ExpectedLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpectedExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedSerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpectedLicensePlateNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingSessionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionLines_AdvanceShippingNoticeLines_AdvanceShi~",
                        column: x => x.AdvanceShippingNoticeLineId,
                        principalTable: "AdvanceShippingNoticeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionLines_ReceivingSessions_ReceivingSessionId",
                        column: x => x.ReceivingSessionId,
                        principalTable: "ReceivingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingSessionScans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceivingSessionId = table.Column<int>(type: "integer", nullable: false),
                    ReceivingSessionLineId = table.Column<int>(type: "integer", nullable: true),
                    ClientOperationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RawScanValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemSku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EnteredQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    UnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PackagingCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    DestinationLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MovementId = table.Column<int>(type: "integer", nullable: true),
                    ReceiptId = table.Column<int>(type: "integer", nullable: true),
                    ReceiptDocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingSessionScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionScans_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionScans_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionScans_ReceivingSessionLines_ReceivingSessio~",
                        column: x => x.ReceivingSessionLineId,
                        principalTable: "ReceivingSessionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingSessionScans_ReceivingSessions_ReceivingSessionId",
                        column: x => x.ReceivingSessionId,
                        principalTable: "ReceivingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionLines_AdvanceShippingNoticeLineId",
                table: "ReceivingSessionLines",
                column: "AdvanceShippingNoticeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionLines_ItemId",
                table: "ReceivingSessionLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionLines_PurchaseOrderLineId",
                table: "ReceivingSessionLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionLines_ReceivingSessionId_LineNumber",
                table: "ReceivingSessionLines",
                columns: new[] { "ReceivingSessionId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessions_AdvanceShippingNoticeId",
                table: "ReceivingSessions",
                column: "AdvanceShippingNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessions_DockLocationId",
                table: "ReceivingSessions",
                column: "DockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessions_PurchaseOrderId",
                table: "ReceivingSessions",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessions_ReceivingLocationId",
                table: "ReceivingSessions",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessions_WarehouseId_SessionReference",
                table: "ReceivingSessions",
                columns: new[] { "WarehouseId", "SessionReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessions_WarehouseId_Status_LastActivityAtUtc",
                table: "ReceivingSessions",
                columns: new[] { "WarehouseId", "Status", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionScans_MovementId",
                table: "ReceivingSessionScans",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionScans_ReceiptId",
                table: "ReceivingSessionScans",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionScans_ReceivingSessionId_ClientOperationId",
                table: "ReceivingSessionScans",
                columns: new[] { "ReceivingSessionId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionScans_ReceivingSessionId_Status",
                table: "ReceivingSessionScans",
                columns: new[] { "ReceivingSessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionScans_ReceivingSessionLineId",
                table: "ReceivingSessionScans",
                column: "ReceivingSessionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSessionScans_RequestedAtUtc",
                table: "ReceivingSessionScans",
                column: "RequestedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceivingSessionScans");

            migrationBuilder.DropTable(
                name: "ReceivingSessionLines");

            migrationBuilder.DropTable(
                name: "ReceivingSessions");
        }
    }
}

#pragma warning restore CA1861
