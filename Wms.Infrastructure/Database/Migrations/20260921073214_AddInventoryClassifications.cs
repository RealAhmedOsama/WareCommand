using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#pragma warning disable CA1861

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryClassificationPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LookbackDays = table.Column<int>(type: "integer", nullable: false),
                    AThresholdPercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    BThresholdPercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    MinimumActivityValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryClassificationPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryClassificationPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryClassificationHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PreviousClassification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MetricValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CumulativePercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ShippedLineCount = table.Column<int>(type: "integer", nullable: false),
                    MovementQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    InventoryValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CriticalityScore = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    LookbackFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LookbackToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PolicyId = table.Column<int>(type: "integer", nullable: true),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: false),
                    CalculationInputVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CalculationRunKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ManualOverrideExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryClassificationHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryClassificationHistories_InventoryClassificationPol~",
                        column: x => x.PolicyId,
                        principalTable: "InventoryClassificationPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryClassificationHistories_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryClassificationHistories_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryClassifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MetricValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CumulativePercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ShippedLineCount = table.Column<int>(type: "integer", nullable: false),
                    MovementQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    InventoryValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CriticalityScore = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    LookbackFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LookbackToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PolicyId = table.Column<int>(type: "integer", nullable: true),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: false),
                    CalculationInputVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CalculationRunKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ManualOverrideReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ManualOverrideExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryClassifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryClassifications_InventoryClassificationPolicies_Po~",
                        column: x => x.PolicyId,
                        principalTable: "InventoryClassificationPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryClassifications_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryClassifications_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassificationHistories_ItemId",
                table: "InventoryClassificationHistories",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassificationHistories_PolicyId",
                table: "InventoryClassificationHistories",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassificationHistories_WarehouseId_ItemId_Calcula~",
                table: "InventoryClassificationHistories",
                columns: new[] { "WarehouseId", "ItemId", "CalculationRunKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassificationHistories_WarehouseId_ItemId_Changed~",
                table: "InventoryClassificationHistories",
                columns: new[] { "WarehouseId", "ItemId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassificationPolicies_WarehouseId_IsActive_Effect~",
                table: "InventoryClassificationPolicies",
                columns: new[] { "WarehouseId", "IsActive", "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassificationPolicies_WarehouseId_PolicyKey",
                table: "InventoryClassificationPolicies",
                columns: new[] { "WarehouseId", "PolicyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassifications_ItemId",
                table: "InventoryClassifications",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassifications_ManualOverrideExpiresAtUtc",
                table: "InventoryClassifications",
                column: "ManualOverrideExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassifications_PolicyId",
                table: "InventoryClassifications",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassifications_WarehouseId_Classification",
                table: "InventoryClassifications",
                columns: new[] { "WarehouseId", "Classification" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryClassifications_WarehouseId_ItemId",
                table: "InventoryClassifications",
                columns: new[] { "WarehouseId", "ItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryClassificationHistories");

            migrationBuilder.DropTable(
                name: "InventoryClassifications");

            migrationBuilder.DropTable(
                name: "InventoryClassificationPolicies");
        }
    }
}

#pragma warning restore CA1861
