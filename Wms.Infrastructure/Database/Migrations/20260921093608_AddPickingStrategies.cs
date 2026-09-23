using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PickingStrategyPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Strategy = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    MaxOrders = table.Column<int>(type: "integer", nullable: false),
                    MaxContainers = table.Column<int>(type: "integer", nullable: false),
                    MaxWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    MaxVolumeCubicMeters = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    WaveTemplateId = table.Column<int>(type: "integer", nullable: true),
                    OrderProfileCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    LocationZoneId = table.Column<int>(type: "integer", nullable: true),
                    PackageProfileCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SequenceMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingStrategyPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingStrategyPolicies_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingStrategyPolicies_Locations_LocationZoneId",
                        column: x => x.LocationZoneId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingStrategyPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingStrategyPolicies_WaveTemplates_WaveTemplateId",
                        column: x => x.WaveTemplateId,
                        principalTable: "WaveTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickingPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PlanNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreationKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Strategy = table.Column<int>(type: "integer", nullable: false),
                    WaveId = table.Column<int>(type: "integer", nullable: true),
                    PolicyId = table.Column<int>(type: "integer", nullable: true),
                    MaxOrders = table.Column<int>(type: "integer", nullable: false),
                    MaxContainers = table.Column<int>(type: "integer", nullable: false),
                    MaxWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    MaxVolumeCubicMeters = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingPlans_PickingStrategyPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "PickingStrategyPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlans_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlans_Waves_WaveId",
                        column: x => x.WaveId,
                        principalTable: "Waves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickingPlanContainers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PickingPlanId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ContainerKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExpectedScanCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetScanRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: true),
                    ZoneLocationId = table.Column<int>(type: "integer", nullable: true),
                    TargetLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScannedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingPlanContainers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingPlanContainers_LicensePlates_TargetLicensePlateId",
                        column: x => x.TargetLicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanContainers_Locations_ZoneLocationId",
                        column: x => x.ZoneLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanContainers_PickingPlans_PickingPlanId",
                        column: x => x.PickingPlanId,
                        principalTable: "PickingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PickingPlanContainers_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickingPlanHandoffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PickingPlanId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PickingPlanContainerId = table.Column<int>(type: "integer", nullable: false),
                    FromZoneLocationId = table.Column<int>(type: "integer", nullable: false),
                    ToZoneLocationId = table.Column<int>(type: "integer", nullable: true),
                    ExpectedContainerScanCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingPlanHandoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingPlanHandoffs_Locations_FromZoneLocationId",
                        column: x => x.FromZoneLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanHandoffs_Locations_ToZoneLocationId",
                        column: x => x.ToZoneLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanHandoffs_PickingPlanContainers_PickingPlanContai~",
                        column: x => x.PickingPlanContainerId,
                        principalTable: "PickingPlanContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanHandoffs_PickingPlans_PickingPlanId",
                        column: x => x.PickingPlanId,
                        principalTable: "PickingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PickingPlanLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PickingPlanId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseWorkLineId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    OrderNumberSnapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: true),
                    ZoneLocationId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: true),
                    ReservationId = table.Column<int>(type: "integer", nullable: true),
                    ReservationAllocationId = table.Column<int>(type: "integer", nullable: true),
                    UnitWeightKg = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    UnitVolumeCubicMeters = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    BatchKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PickingPlanContainerId = table.Column<int>(type: "integer", nullable: true),
                    PickingPlanHandoffId = table.Column<int>(type: "integer", nullable: true),
                    ZoneSequence = table.Column<int>(type: "integer", nullable: false),
                    ContainerSequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingPlanLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_InventoryReservationAllocations_Reservatio~",
                        column: x => x.ReservationAllocationId,
                        principalTable: "InventoryReservationAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_InventoryReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "InventoryReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_LicensePlates_SourceLicensePlateId",
                        column: x => x.SourceLicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_Locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_Locations_ZoneLocationId",
                        column: x => x.ZoneLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_PickingPlanContainers_PickingPlanContainer~",
                        column: x => x.PickingPlanContainerId,
                        principalTable: "PickingPlanContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_PickingPlanHandoffs_PickingPlanHandoffId",
                        column: x => x.PickingPlanHandoffId,
                        principalTable: "PickingPlanHandoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_PickingPlans_PickingPlanId",
                        column: x => x.PickingPlanId,
                        principalTable: "PickingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_WarehouseWorkLines_WarehouseWorkLineId",
                        column: x => x.WarehouseWorkLineId,
                        principalTable: "WarehouseWorkLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingPlanLines_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanContainers_PickingPlanId_ContainerKey",
                table: "PickingPlanContainers",
                columns: new[] { "PickingPlanId", "ContainerKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanContainers_PickingPlanId_Sequence",
                table: "PickingPlanContainers",
                columns: new[] { "PickingPlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanContainers_SalesOrderId",
                table: "PickingPlanContainers",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanContainers_TargetLicensePlateId",
                table: "PickingPlanContainers",
                column: "TargetLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanContainers_ZoneLocationId",
                table: "PickingPlanContainers",
                column: "ZoneLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanHandoffs_FromZoneLocationId",
                table: "PickingPlanHandoffs",
                column: "FromZoneLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanHandoffs_PickingPlanContainerId",
                table: "PickingPlanHandoffs",
                column: "PickingPlanContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanHandoffs_PickingPlanId_Sequence",
                table: "PickingPlanHandoffs",
                columns: new[] { "PickingPlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanHandoffs_PickingPlanId_Status",
                table: "PickingPlanHandoffs",
                columns: new[] { "PickingPlanId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanHandoffs_ToZoneLocationId",
                table: "PickingPlanHandoffs",
                column: "ToZoneLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_InventoryStatusId",
                table: "PickingPlanLines",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_ItemId",
                table: "PickingPlanLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_LotId",
                table: "PickingPlanLines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_PickingPlanContainerId",
                table: "PickingPlanLines",
                column: "PickingPlanContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_PickingPlanHandoffId",
                table: "PickingPlanLines",
                column: "PickingPlanHandoffId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_PickingPlanId_SalesOrderId_ItemId_SourceLo~",
                table: "PickingPlanLines",
                columns: new[] { "PickingPlanId", "SalesOrderId", "ItemId", "SourceLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_PickingPlanId_Sequence",
                table: "PickingPlanLines",
                columns: new[] { "PickingPlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_ReservationAllocationId",
                table: "PickingPlanLines",
                column: "ReservationAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_ReservationId",
                table: "PickingPlanLines",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_SalesOrderId",
                table: "PickingPlanLines",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_SalesOrderLineId",
                table: "PickingPlanLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_SerialNumberId",
                table: "PickingPlanLines",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_SourceLicensePlateId",
                table: "PickingPlanLines",
                column: "SourceLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_SourceLocationId",
                table: "PickingPlanLines",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_WarehouseWorkId",
                table: "PickingPlanLines",
                column: "WarehouseWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_WarehouseWorkLineId",
                table: "PickingPlanLines",
                column: "WarehouseWorkLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlanLines_ZoneLocationId",
                table: "PickingPlanLines",
                column: "ZoneLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlans_PlanNumber",
                table: "PickingPlans",
                column: "PlanNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlans_PolicyId",
                table: "PickingPlans",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlans_WarehouseId_CreationKey",
                table: "PickingPlans",
                columns: new[] { "WarehouseId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlans_WarehouseId_Status_Strategy",
                table: "PickingPlans",
                columns: new[] { "WarehouseId", "Status", "Strategy" });

            migrationBuilder.CreateIndex(
                name: "IX_PickingPlans_WaveId",
                table: "PickingPlans",
                column: "WaveId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingStrategyPolicies_ItemId",
                table: "PickingStrategyPolicies",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingStrategyPolicies_LocationZoneId",
                table: "PickingStrategyPolicies",
                column: "LocationZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingStrategyPolicies_WarehouseId_IsActive_Priority",
                table: "PickingStrategyPolicies",
                columns: new[] { "WarehouseId", "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_PickingStrategyPolicies_WarehouseId_PolicyKey",
                table: "PickingStrategyPolicies",
                columns: new[] { "WarehouseId", "PolicyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingStrategyPolicies_WaveTemplateId",
                table: "PickingStrategyPolicies",
                column: "WaveTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickingPlanLines");

            migrationBuilder.DropTable(
                name: "PickingPlanHandoffs");

            migrationBuilder.DropTable(
                name: "PickingPlanContainers");

            migrationBuilder.DropTable(
                name: "PickingPlans");

            migrationBuilder.DropTable(
                name: "PickingStrategyPolicies");
        }
    }
}

#pragma warning restore CA1861
