using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernedRecommendations : Migration
    {
        private static readonly string[] RecommendationEventIdempotencyColumns =
            ["RecommendationId", "IdempotencyKey"];
        private static readonly string[] RecommendationEventOccurrenceColumns =
            ["RecommendationId", "OccurredAtUtc"];
        private static readonly string[] RecommendationStatusExpiryColumns = ["Status", "ExpiresAtUtc"];
        private static readonly string[] RecommendationWarehouseStatusGeneratedColumns =
            ["WarehouseId", "Status", "GeneratedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GovernedRecommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecommendationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    SourceSnapshotId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceStateFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeterministicBaselineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActionJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NumericFeaturesJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ExpectedImpactLow = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExpectedImpactHigh = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShadowMode = table.Column<bool>(type: "boolean", nullable: false),
                    ShadowComparison = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequiresRevalidation = table.Column<bool>(type: "boolean", nullable: false),
                    CanMutateInventory = table.Column<bool>(type: "boolean", nullable: false),
                    LastDispositionComment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExecutionReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    LastExecutionErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExecutionAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedRecommendations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernedRecommendationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecommendationId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StateFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CommandReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    OutcomeCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedRecommendationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedRecommendationEvents_GovernedRecommendations_Recomm~",
                        column: x => x.RecommendationId,
                        principalTable: "GovernedRecommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedRecommendationEvents_RecommendationId_IdempotencyKey",
                table: "GovernedRecommendationEvents",
                columns: RecommendationEventIdempotencyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedRecommendationEvents_RecommendationId_OccurredAtUtc",
                table: "GovernedRecommendationEvents",
                columns: RecommendationEventOccurrenceColumns);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedRecommendations_RecommendationId",
                table: "GovernedRecommendations",
                column: "RecommendationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedRecommendations_Status_ExpiresAtUtc",
                table: "GovernedRecommendations",
                columns: RecommendationStatusExpiryColumns);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedRecommendations_WarehouseId_Status_GeneratedAtUtc",
                table: "GovernedRecommendations",
                columns: RecommendationWarehouseStatusGeneratedColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernedRecommendationEvents");

            migrationBuilder.DropTable(
                name: "GovernedRecommendations");
        }
    }
}
