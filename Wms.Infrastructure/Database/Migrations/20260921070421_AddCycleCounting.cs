using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#pragma warning disable CA1861

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleCounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CycleCountPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    ItemClass = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FrequencyDays = table.Column<int>(type: "integer", nullable: false),
                    ThresholdQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Blind = table.Column<bool>(type: "boolean", nullable: false),
                    FreezePolicy = table.Column<int>(type: "integer", nullable: false),
                    NextDueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCountPlans", x => x.Id);
                    table.CheckConstraint("CK_CycleCountPlans_ThresholdNonNegative", "\"ThresholdQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_CycleCountPlans_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountPlans_Locations_WarehouseId_LocationId",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountPlans_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CycleCountTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    TaskNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: true),
                    Blind = table.Column<bool>(type: "boolean", nullable: false),
                    FreezePolicy = table.Column<int>(type: "integer", nullable: false),
                    SnapshotAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    StartedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCountTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CycleCountTasks_CycleCountPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "CycleCountPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountTasks_Locations_WarehouseId_LocationId",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountTasks_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountTasks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CycleCountLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    VarianceQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    EmptyLocationCandidate = table.Column<bool>(type: "boolean", nullable: false),
                    EmptyLocationConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCountLines", x => x.Id);
                    table.CheckConstraint("CK_CycleCountLines_NonNegativeQuantities", "\"ExpectedQuantity\" >= 0 AND (\"CountedQuantity\" IS NULL OR \"CountedQuantity\" >= 0)");
                    table.ForeignKey(
                        name: "FK_CycleCountLines_CycleCountTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "CycleCountTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CycleCountLines_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountLines_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountLines_Locations_WarehouseId_LocationId",
                        columns: x => new { x.WarehouseId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountLines_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountLines_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountLines_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_InventoryStatusId",
                table: "CycleCountLines",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_ItemId",
                table: "CycleCountLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_LicensePlateId",
                table: "CycleCountLines",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_LotId",
                table: "CycleCountLines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_SerialNumberId",
                table: "CycleCountLines",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_TaskId_Sequence",
                table: "CycleCountLines",
                columns: new[] { "TaskId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountLines_WarehouseId_LocationId_ItemId_LotId_SerialN~",
                table: "CycleCountLines",
                columns: new[] { "WarehouseId", "LocationId", "ItemId", "LotId", "SerialNumberId", "LicensePlateId", "InventoryStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountPlans_ItemId",
                table: "CycleCountPlans",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountPlans_WarehouseId_IsActive_NextDueAtUtc",
                table: "CycleCountPlans",
                columns: new[] { "WarehouseId", "IsActive", "NextDueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountPlans_WarehouseId_LocationId_ItemId",
                table: "CycleCountPlans",
                columns: new[] { "WarehouseId", "LocationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountPlans_WarehouseId_PlanKey",
                table: "CycleCountPlans",
                columns: new[] { "WarehouseId", "PlanKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountTasks_PlanId",
                table: "CycleCountTasks",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountTasks_TaskKey",
                table: "CycleCountTasks",
                column: "TaskKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountTasks_TaskNumber",
                table: "CycleCountTasks",
                column: "TaskNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountTasks_WarehouseId_LocationId",
                table: "CycleCountTasks",
                columns: new[] { "WarehouseId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountTasks_WarehouseId_Status_SnapshotAtUtc",
                table: "CycleCountTasks",
                columns: new[] { "WarehouseId", "Status", "SnapshotAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountTasks_WarehouseWorkId",
                table: "CycleCountTasks",
                column: "WarehouseWorkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CycleCountLines");

            migrationBuilder.DropTable(
                name: "CycleCountTasks");

            migrationBuilder.DropTable(
                name: "CycleCountPlans");
        }
    }
}

#pragma warning restore CA1861
