using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddValueAddedServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KitDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OutputItemId = table.Column<int>(type: "integer", nullable: false),
                    OutputUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    LocalizedInstructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitDefinitions_Items_OutputItemId",
                        column: x => x.OutputItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KitDefinitionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KitDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ComponentItemId = table.Column<int>(type: "integer", nullable: false),
                    QuantityPerOutput = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ComponentUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubstitutionPolicy = table.Column<int>(type: "integer", nullable: false),
                    ApprovedSubstitutionItemIdsJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitDefinitionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitDefinitionLines_Items_ComponentItemId",
                        column: x => x.ComponentItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitDefinitionLines_KitDefinitions_KitDefinitionId",
                        column: x => x.KitDefinitionId,
                        principalTable: "KitDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValueAddedServiceOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: false),
                    OutputItemId = table.Column<int>(type: "integer", nullable: false),
                    RequestedOutputQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CompletedOutputQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReversedOutputQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    OutputUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    KitDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    KitVersionSnapshot = table.Column<int>(type: "integer", nullable: true),
                    InstructionSnapshot = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    LocalizedInstructionSnapshot = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    StationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LabelTemplateCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    QualityProfileId = table.Column<int>(type: "integer", nullable: true),
                    OutputOwnerKind = table.Column<int>(type: "integer", nullable: false),
                    OutputInventoryOwnerId = table.Column<int>(type: "integer", nullable: true),
                    OutputOwnerCodeSnapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    InputOwnerKind = table.Column<int>(type: "integer", nullable: false),
                    InputInventoryOwnerId = table.Column<int>(type: "integer", nullable: true),
                    InputOwnerCodeSnapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    InputInventoryStatusId = table.Column<int>(type: "integer", nullable: true),
                    InputLotId = table.Column<int>(type: "integer", nullable: true),
                    InputSerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    InputSerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InputLicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExceptionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValueAddedServiceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_InventoryOwners_OutputInventoryOwne~",
                        column: x => x.OutputInventoryOwnerId,
                        principalTable: "InventoryOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_Items_OutputItemId",
                        column: x => x.OutputItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_KitDefinitions_KitDefinitionId",
                        column: x => x.KitDefinitionId,
                        principalTable: "KitDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_Locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValueAddedServiceCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ValueAddedServiceOrderId = table.Column<int>(type: "integer", nullable: false),
                    Operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValueAddedServiceCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceCommands_ValueAddedServiceOrders_ValueAdde~",
                        column: x => x.ValueAddedServiceOrderId,
                        principalTable: "ValueAddedServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValueAddedServiceOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ValueAddedServiceOrderId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ProducedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReversedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: true),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    OwnerKind = table.Column<int>(type: "integer", nullable: false),
                    InventoryOwnerId = table.Column<int>(type: "integer", nullable: true),
                    OwnerCodeSnapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    KitDefinitionLineId = table.Column<int>(type: "integer", nullable: true),
                    ReservationId = table.Column<int>(type: "integer", nullable: true),
                    ReservationAllocationId = table.Column<int>(type: "integer", nullable: true),
                    IsSubstitution = table.Column<bool>(type: "boolean", nullable: false),
                    SubstitutedForItemId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValueAddedServiceOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_InventoryOwners_InventoryOwnerId",
                        column: x => x.InventoryOwnerId,
                        principalTable: "InventoryOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_InventoryReservationAllocations~",
                        column: x => x.ReservationAllocationId,
                        principalTable: "InventoryReservationAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_InventoryReservations_Reservati~",
                        column: x => x.ReservationId,
                        principalTable: "InventoryReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_InventoryStatuses_InventoryStat~",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_KitDefinitionLines_KitDefinitio~",
                        column: x => x.KitDefinitionLineId,
                        principalTable: "KitDefinitionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_Locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceOrderLines_ValueAddedServiceOrders_ValueAd~",
                        column: x => x.ValueAddedServiceOrderId,
                        principalTable: "ValueAddedServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValueAddedServiceTraceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    InputLineId = table.Column<int>(type: "integer", nullable: false),
                    OutputLineId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReversedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValueAddedServiceTraceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceTraceLinks_ValueAddedServiceOrderLines_Inp~",
                        column: x => x.InputLineId,
                        principalTable: "ValueAddedServiceOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceTraceLinks_ValueAddedServiceOrderLines_Out~",
                        column: x => x.OutputLineId,
                        principalTable: "ValueAddedServiceOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValueAddedServiceTraceLinks_ValueAddedServiceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ValueAddedServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KitDefinitionLines_ComponentItemId",
                table: "KitDefinitionLines",
                column: "ComponentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_KitDefinitionLines_KitDefinitionId_Sequence",
                table: "KitDefinitionLines",
                columns: new[] { "KitDefinitionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitDefinitions_Code_Version",
                table: "KitDefinitions",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitDefinitions_OutputItemId_IsActive",
                table: "KitDefinitions",
                columns: new[] { "OutputItemId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceCommands_ValueAddedServiceOrderId_Operatio~",
                table: "ValueAddedServiceCommands",
                columns: new[] { "ValueAddedServiceOrderId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_DestinationLocationId",
                table: "ValueAddedServiceOrderLines",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_InventoryOwnerId",
                table: "ValueAddedServiceOrderLines",
                column: "InventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_InventoryStatusId",
                table: "ValueAddedServiceOrderLines",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_ItemId_Kind_LotId_SerialNumberId",
                table: "ValueAddedServiceOrderLines",
                columns: new[] { "ItemId", "Kind", "LotId", "SerialNumberId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_KitDefinitionLineId",
                table: "ValueAddedServiceOrderLines",
                column: "KitDefinitionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_LicensePlateId",
                table: "ValueAddedServiceOrderLines",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_LotId",
                table: "ValueAddedServiceOrderLines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_OwnerKind_InventoryOwnerId",
                table: "ValueAddedServiceOrderLines",
                columns: new[] { "OwnerKind", "InventoryOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_ReservationAllocationId",
                table: "ValueAddedServiceOrderLines",
                column: "ReservationAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_ReservationId_ReservationAlloca~",
                table: "ValueAddedServiceOrderLines",
                columns: new[] { "ReservationId", "ReservationAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_SerialNumberId",
                table: "ValueAddedServiceOrderLines",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_SourceLocationId",
                table: "ValueAddedServiceOrderLines",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrderLines_ValueAddedServiceOrderId_LineNu~",
                table: "ValueAddedServiceOrderLines",
                columns: new[] { "ValueAddedServiceOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_DestinationLocationId",
                table: "ValueAddedServiceOrders",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_IdempotencyKey",
                table: "ValueAddedServiceOrders",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_KitDefinitionId",
                table: "ValueAddedServiceOrders",
                column: "KitDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_OrderNumber",
                table: "ValueAddedServiceOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_OutputInventoryOwnerId",
                table: "ValueAddedServiceOrders",
                column: "OutputInventoryOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_OutputItemId",
                table: "ValueAddedServiceOrders",
                column: "OutputItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_QualityProfileId",
                table: "ValueAddedServiceOrders",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_SourceLocationId",
                table: "ValueAddedServiceOrders",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_WarehouseId_Status_Type",
                table: "ValueAddedServiceOrders",
                columns: new[] { "WarehouseId", "Status", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceOrders_WarehouseWorkId",
                table: "ValueAddedServiceOrders",
                column: "WarehouseWorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceTraceLinks_InputLineId_OutputLineId",
                table: "ValueAddedServiceTraceLinks",
                columns: new[] { "InputLineId", "OutputLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceTraceLinks_OrderId_IdempotencyKey",
                table: "ValueAddedServiceTraceLinks",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValueAddedServiceTraceLinks_OutputLineId",
                table: "ValueAddedServiceTraceLinks",
                column: "OutputLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValueAddedServiceCommands");

            migrationBuilder.DropTable(
                name: "ValueAddedServiceTraceLinks");

            migrationBuilder.DropTable(
                name: "ValueAddedServiceOrderLines");

            migrationBuilder.DropTable(
                name: "KitDefinitionLines");

            migrationBuilder.DropTable(
                name: "ValueAddedServiceOrders");

            migrationBuilder.DropTable(
                name: "KitDefinitions");
        }
    }
}
#pragma warning restore CA1861
