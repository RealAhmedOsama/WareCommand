using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnsAndDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RmaNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: true),
                    ShipmentId = table.Column<int>(type: "integer", nullable: true),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    ReturnLocationId = table.Column<int>(type: "integer", nullable: false),
                    Unplanned = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AuthorizedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnAuthorizations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAuthorizations_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAuthorizations_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAuthorizations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnAuthorizationId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ReturnCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnCommands_ReturnAuthorizations_ReturnAuthorizationId",
                        column: x => x.ReturnAuthorizationId,
                        principalTable: "ReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnAuthorizationId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: true),
                    ShipmentLineId = table.Column<int>(type: "integer", nullable: true),
                    ExpectedLotId = table.Column<int>(type: "integer", nullable: true),
                    ExpectedSerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    DisposedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnLines_ReturnAuthorizations_ReturnAuthorizationId",
                        column: x => x.ReturnAuthorizationId,
                        principalTable: "ReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnLines_ShipmentLines_ShipmentLineId",
                        column: x => x.ShipmentLineId,
                        principalTable: "ShipmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnAuthorizationId = table.Column<int>(type: "integer", nullable: false),
                    ReturnLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    DisposedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReturnLocationId = table.Column<int>(type: "integer", nullable: false),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnReceipts_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnReceipts_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnReceipts_ReturnAuthorizations_ReturnAuthorizationId",
                        column: x => x.ReturnAuthorizationId,
                        principalTable: "ReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnReceipts_ReturnLines_ReturnLineId",
                        column: x => x.ReturnLineId,
                        principalTable: "ReturnLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnDispositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnAuthorizationId = table.Column<int>(type: "integer", nullable: false),
                    ReturnReceiptId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: true),
                    DestinationInventoryStatusId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DisposedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnDispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnDispositions_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnDispositions_ReturnAuthorizations_ReturnAuthorization~",
                        column: x => x.ReturnAuthorizationId,
                        principalTable: "ReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnDispositions_ReturnReceipts_ReturnReceiptId",
                        column: x => x.ReturnReceiptId,
                        principalTable: "ReturnReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAuthorizations_CustomerId",
                table: "ReturnAuthorizations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAuthorizations_SalesOrderId",
                table: "ReturnAuthorizations",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAuthorizations_ShipmentId",
                table: "ReturnAuthorizations",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAuthorizations_WarehouseId_RmaNumber",
                table: "ReturnAuthorizations",
                columns: new[] { "WarehouseId", "RmaNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAuthorizations_WarehouseId_Status",
                table: "ReturnAuthorizations",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnCommands_ReturnAuthorizationId_Operation_IdempotencyK~",
                table: "ReturnCommands",
                columns: new[] { "ReturnAuthorizationId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnDispositions_DestinationLocationId",
                table: "ReturnDispositions",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnDispositions_ReturnAuthorizationId",
                table: "ReturnDispositions",
                column: "ReturnAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnDispositions_ReturnReceiptId_DisposedAtUtc",
                table: "ReturnDispositions",
                columns: new[] { "ReturnReceiptId", "DisposedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnLines_ItemId",
                table: "ReturnLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnLines_ReturnAuthorizationId_ItemId_SalesOrderLineId",
                table: "ReturnLines",
                columns: new[] { "ReturnAuthorizationId", "ItemId", "SalesOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnLines_SalesOrderLineId",
                table: "ReturnLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnLines_ShipmentLineId",
                table: "ReturnLines",
                column: "ShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnReceipts_ItemId",
                table: "ReturnReceipts",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnReceipts_LicensePlateId",
                table: "ReturnReceipts",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnReceipts_ReturnAuthorizationId",
                table: "ReturnReceipts",
                column: "ReturnAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnReceipts_ReturnLineId_ItemId_LotId_SerialNumberId_Inv~",
                table: "ReturnReceipts",
                columns: new[] { "ReturnLineId", "ItemId", "LotId", "SerialNumberId", "InventoryStatusId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnCommands");

            migrationBuilder.DropTable(
                name: "ReturnDispositions");

            migrationBuilder.DropTable(
                name: "ReturnReceipts");

            migrationBuilder.DropTable(
                name: "ReturnLines");

            migrationBuilder.DropTable(
                name: "ReturnAuthorizations");
        }
    }
}
