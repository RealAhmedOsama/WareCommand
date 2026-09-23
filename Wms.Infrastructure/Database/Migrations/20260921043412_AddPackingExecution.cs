using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackingStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SupportedDevices = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PrinterProfile = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ScaleProfile = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllowedUserIds = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PermissionProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingStations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackingStations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PackingStationId = table.Column<int>(type: "integer", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: true),
                    StagingLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingSessions_LicensePlates_StagingLicensePlateId",
                        column: x => x.StagingLicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackingSessions_PackingStations_PackingStationId",
                        column: x => x.PackingStationId,
                        principalTable: "PackingStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackingSessions_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackingSessions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PackingSessionId = table.Column<int>(type: "integer", nullable: false),
                    TargetLicensePlateId = table.Column<int>(type: "integer", nullable: false),
                    PackageType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: true),
                    AllowConsolidated = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpectedWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    ActualWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    WeightToleranceKg = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    WeightTolerancePercent = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    LengthCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    WidthCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    VolumeCubicMeters = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    LabelReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    ClosedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPackages", x => x.Id);
                    table.CheckConstraint("CK_ShipmentPackages_Tolerances", "\"ExpectedWeightKg\" IS NULL OR \"ExpectedWeightKg\" >= 0");
                    table.ForeignKey(
                        name: "FK_ShipmentPackages_LicensePlates_TargetLicensePlateId",
                        column: x => x.TargetLicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackages_PackingSessions_PackingSessionId",
                        column: x => x.PackingSessionId,
                        principalTable: "PackingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackages_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackages_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackingCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackingSessionId = table.Column<int>(type: "integer", nullable: true),
                    ShipmentPackageId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_PackingCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingCommands_PackingSessions_PackingSessionId",
                        column: x => x.PackingSessionId,
                        principalTable: "PackingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackingCommands_ShipmentPackages_ShipmentPackageId",
                        column: x => x.ShipmentPackageId,
                        principalTable: "ShipmentPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentPackageContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentPackageId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    SourceLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    ExpectedWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPackageContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_LicensePlates_SourceLicensePlateId",
                        column: x => x.SourceLicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_Locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageContents_ShipmentPackages_ShipmentPackageId",
                        column: x => x.ShipmentPackageId,
                        principalTable: "ShipmentPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackingCommands_ExecutedAtUtc",
                table: "PackingCommands",
                column: "ExecutedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PackingCommands_PackingSessionId_Operation_IdempotencyKey",
                table: "PackingCommands",
                columns: new[] { "PackingSessionId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingCommands_ShipmentPackageId_Operation_IdempotencyKey",
                table: "PackingCommands",
                columns: new[] { "ShipmentPackageId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingSessions_PackingStationId_Status",
                table: "PackingSessions",
                columns: new[] { "PackingStationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingSessions_SalesOrderId",
                table: "PackingSessions",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingSessions_SessionNumber",
                table: "PackingSessions",
                column: "SessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingSessions_StagingLicensePlateId",
                table: "PackingSessions",
                column: "StagingLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingSessions_WarehouseId_Status_StartedAtUtc",
                table: "PackingSessions",
                columns: new[] { "WarehouseId", "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingStations_LocationId",
                table: "PackingStations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingStations_WarehouseId_Code",
                table: "PackingStations",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingStations_WarehouseId_Status",
                table: "PackingStations",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_InventoryStatusId",
                table: "ShipmentPackageContents",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_ItemId",
                table: "ShipmentPackageContents",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_LotId",
                table: "ShipmentPackageContents",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_SalesOrderLineId_ItemId",
                table: "ShipmentPackageContents",
                columns: new[] { "SalesOrderLineId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_SerialNumberId",
                table: "ShipmentPackageContents",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_ShipmentPackageId_SalesOrderLineId_~",
                table: "ShipmentPackageContents",
                columns: new[] { "ShipmentPackageId", "SalesOrderLineId", "ItemId", "LotId", "SerialNumberId", "SourceLicensePlateId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_SourceLicensePlateId",
                table: "ShipmentPackageContents",
                column: "SourceLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageContents_SourceLocationId",
                table: "ShipmentPackageContents",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackages_PackingSessionId_Status",
                table: "ShipmentPackages",
                columns: new[] { "PackingSessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackages_SalesOrderId_Status",
                table: "ShipmentPackages",
                columns: new[] { "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackages_TargetLicensePlateId",
                table: "ShipmentPackages",
                column: "TargetLicensePlateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackages_WarehouseId_PackageNumber",
                table: "ShipmentPackages",
                columns: new[] { "WarehouseId", "PackageNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackingCommands");

            migrationBuilder.DropTable(
                name: "ShipmentPackageContents");

            migrationBuilder.DropTable(
                name: "ShipmentPackages");

            migrationBuilder.DropTable(
                name: "PackingSessions");

            migrationBuilder.DropTable(
                name: "PackingStations");
        }
    }
}
