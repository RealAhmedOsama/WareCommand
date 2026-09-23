using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundWaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WaveTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Limit = table.Column<int>(type: "integer", nullable: false),
                    ReleaseToWarehouse = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleCron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestedShipDateFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    RequestedShipDateTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    MinimumPriority = table.Column<int>(type: "integer", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaveTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaveTemplates_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaveTemplates_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Waves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    WaveNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreationKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    TemplateKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    PlannedStartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlannedReleaseAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriteriaJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CapacityLimit = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastProcessIdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    LastProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Waves_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Waves_WaveTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "WaveTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WaveLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WaveId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BackorderQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ReservationId = table.Column<int>(type: "integer", nullable: true),
                    WorkCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaveLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaveLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaveLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaveLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaveLines_Waves_WaveId",
                        column: x => x.WaveId,
                        principalTable: "Waves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WaveProcessingHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WaveId = table.Column<int>(type: "integer", nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ItemsExamined = table.Column<int>(type: "integer", nullable: false),
                    ItemsSucceeded = table.Column<int>(type: "integer", nullable: false),
                    ItemsFailed = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DetailsJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaveProcessingHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaveProcessingHistory_Waves_WaveId",
                        column: x => x.WaveId,
                        principalTable: "Waves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WaveLines_ItemId",
                table: "WaveLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WaveLines_SalesOrderId_Status",
                table: "WaveLines",
                columns: new[] { "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WaveLines_SalesOrderLineId_Status",
                table: "WaveLines",
                columns: new[] { "SalesOrderLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WaveLines_WaveId_SalesOrderLineId",
                table: "WaveLines",
                columns: new[] { "WaveId", "SalesOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaveProcessingHistory_WaveId_IdempotencyKey",
                table: "WaveProcessingHistory",
                columns: new[] { "WaveId", "IdempotencyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_WaveProcessingHistory_WaveId_Step_Attempt",
                table: "WaveProcessingHistory",
                columns: new[] { "WaveId", "Step", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waves_TemplateId",
                table: "Waves",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Waves_WarehouseId_CreationKey",
                table: "Waves",
                columns: new[] { "WarehouseId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waves_WarehouseId_Status_PlannedReleaseAtUtc",
                table: "Waves",
                columns: new[] { "WarehouseId", "Status", "PlannedReleaseAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Waves_WaveNumber",
                table: "Waves",
                column: "WaveNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaveTemplates_CustomerId",
                table: "WaveTemplates",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_WaveTemplates_WarehouseId_IsActive_TriggerType",
                table: "WaveTemplates",
                columns: new[] { "WarehouseId", "IsActive", "TriggerType" });

            migrationBuilder.CreateIndex(
                name: "IX_WaveTemplates_WarehouseId_TemplateKey",
                table: "WaveTemplates",
                columns: new[] { "WarehouseId", "TemplateKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WaveLines");

            migrationBuilder.DropTable(
                name: "WaveProcessingHistory");

            migrationBuilder.DropTable(
                name: "Waves");

            migrationBuilder.DropTable(
                name: "WaveTemplates");
        }
    }
}

#pragma warning restore CA1861
