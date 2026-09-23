using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#pragma warning disable CA1861

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSlottingAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlottingPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LookbackDays = table.Column<int>(type: "integer", nullable: false),
                    VelocityWeight = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    TravelWeight = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    SpaceWeight = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    ReplenishmentWeight = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    AffinityWeight = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    MaxRecommendationsPerItem = table.Column<int>(type: "integer", nullable: false),
                    RecommendationExpiryDays = table.Column<int>(type: "integer", nullable: false),
                    AllowedLocationTypes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlottingPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlottingPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlottingRecommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecommendationKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    TargetLocationId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    CurrentScore = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    ExpectedTravelReduction = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    ExpectedReplenishmentReduction = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    ExpectedCongestionReduction = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    SourcePeriodFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourcePeriodToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceDataVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FactorSnapshotJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ConstraintSnapshotJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    AnalysisRunKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    WorkId = table.Column<int>(type: "integer", nullable: true),
                    WorkCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlottingRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlottingRecommendations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlottingRecommendations_Locations_WarehouseId_SourceLocatio~",
                        columns: x => new { x.WarehouseId, x.SourceLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlottingRecommendations_Locations_WarehouseId_TargetLocatio~",
                        columns: x => new { x.WarehouseId, x.TargetLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlottingRecommendations_WarehouseWorks_WorkId",
                        column: x => x.WorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlottingRecommendations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlottingPolicies_WarehouseId_IsActive_EffectiveFromUtc_Effe~",
                table: "SlottingPolicies",
                columns: new[] { "WarehouseId", "IsActive", "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SlottingPolicies_WarehouseId_PolicyKey",
                table: "SlottingPolicies",
                columns: new[] { "WarehouseId", "PolicyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlottingRecommendations_ItemId",
                table: "SlottingRecommendations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SlottingRecommendations_RecommendationKey",
                table: "SlottingRecommendations",
                column: "RecommendationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlottingRecommendations_WarehouseId_AnalysisRunKey",
                table: "SlottingRecommendations",
                columns: new[] { "WarehouseId", "AnalysisRunKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SlottingRecommendations_WarehouseId_SourceLocationId",
                table: "SlottingRecommendations",
                columns: new[] { "WarehouseId", "SourceLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SlottingRecommendations_WarehouseId_Status_ExpiresAtUtc_Ite~",
                table: "SlottingRecommendations",
                columns: new[] { "WarehouseId", "Status", "ExpiresAtUtc", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SlottingRecommendations_WarehouseId_TargetLocationId",
                table: "SlottingRecommendations",
                columns: new[] { "WarehouseId", "TargetLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SlottingRecommendations_WorkId",
                table: "SlottingRecommendations",
                column: "WorkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlottingPolicies");

            migrationBuilder.DropTable(
                name: "SlottingRecommendations");
        }
    }
}
#pragma warning restore CA1861
