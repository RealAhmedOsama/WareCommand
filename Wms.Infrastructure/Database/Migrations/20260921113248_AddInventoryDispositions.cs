using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryDispositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryDispositionPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    ItemCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WarningDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumShelfLifeDays = table.Column<int>(type: "integer", nullable: false),
                    RequireApprovalForScrap = table.Column<bool>(type: "boolean", nullable: false),
                    RequireWitnessForDestruction = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryDispositionPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryDispositionPolicies_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositionPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryDispositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DispositionNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    StockId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    SourceInventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    TargetInventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CompletedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WitnessUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovalRequired = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DestinationStockId = table.Column<int>(type: "integer", nullable: true),
                    OutboundMovementId = table.Column<int>(type: "integer", nullable: true),
                    InboundMovementId = table.Column<int>(type: "integer", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryDispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_InventoryStatuses_SourceInventoryStat~",
                        column: x => x.SourceInventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_InventoryStatuses_TargetInventoryStat~",
                        column: x => x.TargetInventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_Locations_WarehouseId_LocationId",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_Stock_StockId",
                        column: x => x.StockId,
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDispositions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryRecallCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CaseNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRecallCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryRecallCases_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryRecallCases_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryRecallCases_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryRecallCases_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryRecallCases_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositionPolicies_ItemId",
                table: "InventoryDispositionPolicies",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositionPolicies_WarehouseId_IsActive_Effective~",
                table: "InventoryDispositionPolicies",
                columns: new[] { "WarehouseId", "IsActive", "EffectiveFromUtc", "EffectiveToUtc", "Priority", "ItemId", "ItemCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositionPolicies_WarehouseId_PolicyKey",
                table: "InventoryDispositionPolicies",
                columns: new[] { "WarehouseId", "PolicyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_DispositionNumber",
                table: "InventoryDispositions",
                column: "DispositionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_ItemId",
                table: "InventoryDispositions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_LicensePlateId",
                table: "InventoryDispositions",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_LotId",
                table: "InventoryDispositions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_SerialNumberId",
                table: "InventoryDispositions",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_SourceInventoryStatusId",
                table: "InventoryDispositions",
                column: "SourceInventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_StockId",
                table: "InventoryDispositions",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_TargetInventoryStatusId",
                table: "InventoryDispositions",
                column: "TargetInventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_WarehouseId_IdempotencyKey",
                table: "InventoryDispositions",
                columns: new[] { "WarehouseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_WarehouseId_LocationId",
                table: "InventoryDispositions",
                columns: new[] { "WarehouseId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDispositions_WarehouseId_Status_Kind_RequestedAtUtc",
                table: "InventoryDispositions",
                columns: new[] { "WarehouseId", "Status", "Kind", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRecallCases_CaseNumber",
                table: "InventoryRecallCases",
                column: "CaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRecallCases_ItemId",
                table: "InventoryRecallCases",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRecallCases_LicensePlateId",
                table: "InventoryRecallCases",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRecallCases_LotId",
                table: "InventoryRecallCases",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRecallCases_SerialNumberId",
                table: "InventoryRecallCases",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRecallCases_WarehouseId_IdempotencyKey",
                table: "InventoryRecallCases",
                columns: new[] { "WarehouseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRecallCases_WarehouseId_Status_CreatedAtUtc",
                table: "InventoryRecallCases",
                columns: new[] { "WarehouseId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryDispositionPolicies");

            migrationBuilder.DropTable(
                name: "InventoryDispositions");

            migrationBuilder.DropTable(
                name: "InventoryRecallCases");
        }
    }
}

#pragma warning restore CA1861
