using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#pragma warning disable CA1861

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboundExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ExceptionNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    QueueCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdvanceShippingNoticeId = table.Column<int>(type: "integer", nullable: true),
                    AdvanceShippingNoticeLineId = table.Column<int>(type: "integer", nullable: true),
                    ReceiptId = table.Column<int>(type: "integer", nullable: true),
                    ReceiptLineId = table.Column<int>(type: "integer", nullable: true),
                    ReceivingSessionId = table.Column<int>(type: "integer", nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    StagingLocationId = table.Column<int>(type: "integer", nullable: true),
                    ExpectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    ActualBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    VarianceBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AttachmentReferences = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedTeamCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AssignedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewStartedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReviewStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<int>(type: "integer", nullable: true),
                    ResolutionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_AdvanceShippingNoticeLines_AdvanceShippin~",
                        column: x => x.AdvanceShippingNoticeLineId,
                        principalTable: "AdvanceShippingNoticeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_AdvanceShippingNotices_AdvanceShippingNot~",
                        column: x => x.AdvanceShippingNoticeId,
                        principalTable: "AdvanceShippingNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_Locations_StagingLocationId",
                        column: x => x.StagingLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_ReceiptLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_ReceivingSessions_ReceivingSessionId",
                        column: x => x.ReceivingSessionId,
                        principalTable: "ReceivingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboundExceptions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_AdvanceShippingNoticeId",
                table: "InboundExceptions",
                column: "AdvanceShippingNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_AdvanceShippingNoticeLineId",
                table: "InboundExceptions",
                column: "AdvanceShippingNoticeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_ExceptionNumber",
                table: "InboundExceptions",
                column: "ExceptionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_ItemId",
                table: "InboundExceptions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_LicensePlateId",
                table: "InboundExceptions",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_ReceiptId_ReceiptLineId",
                table: "InboundExceptions",
                columns: new[] { "ReceiptId", "ReceiptLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_ReceiptLineId",
                table: "InboundExceptions",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_ReceivingSessionId_Status",
                table: "InboundExceptions",
                columns: new[] { "ReceivingSessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_StagingLocationId",
                table: "InboundExceptions",
                column: "StagingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_WarehouseId_DueAtUtc_Status",
                table: "InboundExceptions",
                columns: new[] { "WarehouseId", "DueAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_WarehouseId_IdempotencyKey",
                table: "InboundExceptions",
                columns: new[] { "WarehouseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_WarehouseId_Status_QueueCode_Severity",
                table: "InboundExceptions",
                columns: new[] { "WarehouseId", "Status", "QueueCode", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundExceptions_WarehouseWorkId",
                table: "InboundExceptions",
                column: "WarehouseWorkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundExceptions");
        }
    }
}

#pragma warning restore CA1861
