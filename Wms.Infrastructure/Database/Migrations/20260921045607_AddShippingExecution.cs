using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carriers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TrackingUrlTemplate = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carriers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CarrierId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupportsTracking = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsLabel = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierServices_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    CarrierId = table.Column<int>(type: "integer", nullable: true),
                    CarrierServiceId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ShipToRecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShipToCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ShipToRegion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipToCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipToPostalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ShipToAddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToAddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToDeliveryInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PlannedShipAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualShipAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    TrackingStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastTrackingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExceptionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_CarrierServices_CarrierServiceId",
                        column: x => x.CarrierServiceId,
                        principalTable: "CarrierServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ShipmentCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentCommands_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLines_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentLoads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentId = table.Column<int>(type: "integer", nullable: false),
                    DockLocationId = table.Column<int>(type: "integer", nullable: true),
                    TrailerNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RouteReference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentLoads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentLoads_Locations_DockLocationId",
                        column: x => x.DockLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLoads_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentTrackingEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentTrackingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentTrackingEvents_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentPackageLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentId = table.Column<int>(type: "integer", nullable: false),
                    ShipmentPackageId = table.Column<int>(type: "integer", nullable: false),
                    ShipmentLoadId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LoadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LoadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShippedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPackageLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageLinks_ShipmentLoads_ShipmentLoadId",
                        column: x => x.ShipmentLoadId,
                        principalTable: "ShipmentLoads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageLinks_ShipmentPackages_ShipmentPackageId",
                        column: x => x.ShipmentPackageId,
                        principalTable: "ShipmentPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageLinks_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_Code",
                table: "Carriers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierServices_CarrierId_Code",
                table: "CarrierServices",
                columns: new[] { "CarrierId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCommands_ShipmentId_Operation_IdempotencyKey",
                table: "ShipmentCommands",
                columns: new[] { "ShipmentId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLines_ItemId",
                table: "ShipmentLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLines_SalesOrderLineId",
                table: "ShipmentLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLines_ShipmentId_SalesOrderLineId_ItemId",
                table: "ShipmentLines",
                columns: new[] { "ShipmentId", "SalesOrderLineId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLoads_DockLocationId",
                table: "ShipmentLoads",
                column: "DockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLoads_ShipmentId_Status",
                table: "ShipmentLoads",
                columns: new[] { "ShipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageLinks_ShipmentId_ShipmentPackageId",
                table: "ShipmentPackageLinks",
                columns: new[] { "ShipmentId", "ShipmentPackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageLinks_ShipmentLoadId",
                table: "ShipmentPackageLinks",
                column: "ShipmentLoadId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageLinks_ShipmentPackageId",
                table: "ShipmentPackageLinks",
                column: "ShipmentPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CarrierId",
                table: "Shipments",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CarrierServiceId",
                table: "Shipments",
                column: "CarrierServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ExternalReference",
                table: "Shipments",
                column: "ExternalReference");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_WarehouseId_ShipmentNumber",
                table: "Shipments",
                columns: new[] { "WarehouseId", "ShipmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_WarehouseId_Status",
                table: "Shipments",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentTrackingEvents_ShipmentId_OccurredAtUtc",
                table: "ShipmentTrackingEvents",
                columns: new[] { "ShipmentId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentCommands");

            migrationBuilder.DropTable(
                name: "ShipmentLines");

            migrationBuilder.DropTable(
                name: "ShipmentPackageLinks");

            migrationBuilder.DropTable(
                name: "ShipmentTrackingEvents");

            migrationBuilder.DropTable(
                name: "ShipmentLoads");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropTable(
                name: "CarrierServices");

            migrationBuilder.DropTable(
                name: "Carriers");
        }
    }
}
