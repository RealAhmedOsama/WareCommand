using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861 // EF-generated migration index arrays are fixed migration metadata.

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableForecasting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForecastRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Granularity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SelectedModel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceCutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InputPeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    InputPeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    HorizonPeriods = table.Column<int>(type: "integer", nullable: false),
                    TrainingWindowPeriods = table.Column<int>(type: "integer", nullable: false),
                    MovingAverageWindow = table.Column<int>(type: "integer", nullable: false),
                    OnHandQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    OnOrderQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    InTransitQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    LeadTimePeriods = table.Column<int>(type: "integer", nullable: false),
                    SafetyStockQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    MeanAbsoluteError = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    MeanAbsolutePercentageError = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    Bias = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    BacktestEvaluatedPeriods = table.Column<int>(type: "integer", nullable: false),
                    BacktestScoresJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    DataQualityFlagsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InsufficientDataReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProjectedStockoutPeriod = table.Column<DateOnly>(type: "date", nullable: true),
                    DaysOfSupply = table.Column<decimal>(type: "numeric(28,4)", nullable: true),
                    RiskLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastRuns_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForecastRuns_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForecastOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ForecastRunId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastOverrides_ForecastRuns_ForecastRunId",
                        column: x => x.ForecastRunId,
                        principalTable: "ForecastRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForecastRunPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ForecastRunId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualDemand = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    ForecastQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    LowerBound = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    UpperBound = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    WasStockoutCensored = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastRunPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastRunPoints_ForecastRuns_ForecastRunId",
                        column: x => x.ForecastRunId,
                        principalTable: "ForecastRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastOverrides_ForecastRunId_PeriodStart",
                table: "ForecastOverrides",
                columns: new[] { "ForecastRunId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastOverrides_ForecastRunId_PeriodStart_Version",
                table: "ForecastOverrides",
                columns: new[] { "ForecastRunId", "PeriodStart", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForecastRunPoints_ForecastRunId_PeriodStart",
                table: "ForecastRunPoints",
                columns: new[] { "ForecastRunId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForecastRuns_ItemId",
                table: "ForecastRuns",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ForecastRuns_WarehouseId_Granularity_CreatedAtUtc",
                table: "ForecastRuns",
                columns: new[] { "WarehouseId", "Granularity", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastRuns_WarehouseId_ItemId_CreatedAtUtc",
                table: "ForecastRuns",
                columns: new[] { "WarehouseId", "ItemId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastRuns_WarehouseId_ItemId_Granularity_HorizonPeriods_~",
                table: "ForecastRuns",
                columns: new[] { "WarehouseId", "ItemId", "Granularity", "HorizonPeriods", "InputFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForecastOverrides");

            migrationBuilder.DropTable(
                name: "ForecastRunPoints");

            migrationBuilder.DropTable(
                name: "ForecastRuns");
        }
    }
}
