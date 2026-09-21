using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundExceptions",
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
                    SalesOrderId = table.Column<int>(type: "integer", nullable: true),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: true),
                    InventoryReservationId = table.Column<int>(type: "integer", nullable: true),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: true),
                    WarehouseWorkLineId = table.Column<int>(type: "integer", nullable: true),
                    ShipmentId = table.Column<int>(type: "integer", nullable: true),
                    ShipmentPackageId = table.Column<int>(type: "integer", nullable: true),
                    ShipmentLoadId = table.Column<int>(type: "integer", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
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
                    ResolutionDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_InventoryReservations_InventoryReservati~",
                        column: x => x.InventoryReservationId,
                        principalTable: "InventoryReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_ShipmentLoads_ShipmentLoadId",
                        column: x => x.ShipmentLoadId,
                        principalTable: "ShipmentLoads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_ShipmentPackages_ShipmentPackageId",
                        column: x => x.ShipmentPackageId,
                        principalTable: "ShipmentPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_WarehouseWorkLines_WarehouseWorkLineId",
                        column: x => x.WarehouseWorkLineId,
                        principalTable: "WarehouseWorkLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundExceptions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboundExceptionCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OutboundExceptionId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_OutboundExceptionCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundExceptionCommands_OutboundExceptions_OutboundExcept~",
                        column: x => x.OutboundExceptionId,
                        principalTable: "OutboundExceptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptionCommands_OutboundExceptionId_Operation_Ide~",
                table: "OutboundExceptionCommands",
                columns: new[] { "OutboundExceptionId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_ExceptionNumber",
                table: "OutboundExceptions",
                column: "ExceptionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_InventoryReservationId",
                table: "OutboundExceptions",
                column: "InventoryReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_ItemId",
                table: "OutboundExceptions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_LicensePlateId",
                table: "OutboundExceptions",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_LocationId",
                table: "OutboundExceptions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_LotId",
                table: "OutboundExceptions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_SalesOrderId_SalesOrderLineId",
                table: "OutboundExceptions",
                columns: new[] { "SalesOrderId", "SalesOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_SalesOrderLineId",
                table: "OutboundExceptions",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_SerialNumberId",
                table: "OutboundExceptions",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_ShipmentId_Status",
                table: "OutboundExceptions",
                columns: new[] { "ShipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_ShipmentLoadId",
                table: "OutboundExceptions",
                column: "ShipmentLoadId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_ShipmentPackageId",
                table: "OutboundExceptions",
                column: "ShipmentPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_WarehouseId_DueAtUtc_Status",
                table: "OutboundExceptions",
                columns: new[] { "WarehouseId", "DueAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_WarehouseId_IdempotencyKey",
                table: "OutboundExceptions",
                columns: new[] { "WarehouseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_WarehouseId_Status_QueueCode_Severity",
                table: "OutboundExceptions",
                columns: new[] { "WarehouseId", "Status", "QueueCode", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_WarehouseWorkId",
                table: "OutboundExceptions",
                column: "WarehouseWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundExceptions_WarehouseWorkLineId",
                table: "OutboundExceptions",
                column: "WarehouseWorkLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboundExceptionCommands");

            migrationBuilder.DropTable(
                name: "OutboundExceptions");
        }
    }
}
