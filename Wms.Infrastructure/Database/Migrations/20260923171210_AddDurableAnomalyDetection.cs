using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableAnomalyDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnomalyDetectionRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    SourceWindowFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceWindowToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleVersionsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DataQualityFlagsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SourceSignals = table.Column<int>(type: "integer", nullable: false),
                    FindingsCreated = table.Column<int>(type: "integer", nullable: false),
                    FindingsReused = table.Column<int>(type: "integer", nullable: false),
                    StartedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyDetectionRuns", x => x.Id);
                    table.CheckConstraint("CK_AnomalyDetectionRuns_Window", "\"SourceWindowToUtc\" >= \"SourceWindowFromUtc\"");
                    table.ForeignKey(
                        name: "FK_AnomalyDetectionRuns_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnomalyRuleConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UseExternalNotifications = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyRuleConfigurations", x => x.Id);
                    table.CheckConstraint("CK_AnomalyRuleConfigurations_Threshold", "\"Threshold\" >= 0");
                    table.CheckConstraint("CK_AnomalyRuleConfigurations_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AnomalyRuleConfigurations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnomalyFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    RuleKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RuleVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FirstDetectionRunId = table.Column<int>(type: "integer", nullable: false),
                    SourceWindowFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceWindowToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ObservedValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ExpectedValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstDetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedToUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedTeamCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AssignedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuppressionExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuppressedFromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyFindings", x => x.Id);
                    table.CheckConstraint("CK_AnomalyFindings_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_AnomalyFindings_AnomalyDetectionRuns_FirstDetectionRunId",
                        column: x => x.FirstDetectionRunId,
                        principalTable: "AnomalyDetectionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnomalyFindings_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnomalyFindingHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FindingId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AssignedToUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedTeamCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EvidenceReferencesJson = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    SuppressionExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyFindingHistory", x => x.Id);
                    table.CheckConstraint("CK_AnomalyFindingHistory_Sequence", "\"Sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_AnomalyFindingHistory_AnomalyFindings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "AnomalyFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnomalyFindingObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<int>(type: "integer", nullable: false),
                    FindingId = table.Column<int>(type: "integer", nullable: false),
                    SourceWindowFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceWindowToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourcePresent = table.Column<bool>(type: "boolean", nullable: false),
                    IsAnomalous = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedValue = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    ExpectedValue = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    Threshold = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    Explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyFindingObservations", x => x.Id);
                    table.CheckConstraint("CK_AnomalyFindingObservations_Window", "\"SourceWindowToUtc\" >= \"SourceWindowFromUtc\"");
                    table.ForeignKey(
                        name: "FK_AnomalyFindingObservations_AnomalyDetectionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AnomalyDetectionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnomalyFindingObservations_AnomalyFindings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "AnomalyFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyDetectionRuns_WarehouseId_CompletedAtUtc",
                table: "AnomalyDetectionRuns",
                columns: new[] { "WarehouseId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyDetectionRuns_WarehouseId_InputFingerprint",
                table: "AnomalyDetectionRuns",
                columns: new[] { "WarehouseId", "InputFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFindingHistory_FindingId_Sequence",
                table: "AnomalyFindingHistory",
                columns: new[] { "FindingId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFindingObservations_FindingId_RecordedAtUtc",
                table: "AnomalyFindingObservations",
                columns: new[] { "FindingId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFindingObservations_RunId_FindingId",
                table: "AnomalyFindingObservations",
                columns: new[] { "RunId", "FindingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFindings_Fingerprint",
                table: "AnomalyFindings",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFindings_FirstDetectionRunId",
                table: "AnomalyFindings",
                column: "FirstDetectionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFindings_WarehouseId_RuleKind_Status",
                table: "AnomalyFindings",
                columns: new[] { "WarehouseId", "RuleKind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFindings_WarehouseId_Status_FirstDetectedAtUtc",
                table: "AnomalyFindings",
                columns: new[] { "WarehouseId", "Status", "FirstDetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyRuleConfigurations_RuleKind_ScopeKey_Version",
                table: "AnomalyRuleConfigurations",
                columns: new[] { "RuleKind", "ScopeKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyRuleConfigurations_WarehouseId_RuleKind_Version",
                table: "AnomalyRuleConfigurations",
                columns: new[] { "WarehouseId", "RuleKind", "Version" });

            migrationBuilder.Sql(
                """
                INSERT INTO "AnomalyRuleConfigurations"
                    ("RuleKind", "WarehouseId", "ScopeKey", "Version", "Threshold", "IsEnabled",
                     "UseExternalNotifications", "CreatedByUserId", "CreatedAtUtc")
                VALUES
                    ('InventoryAdjustment', NULL, 'global', 1, 5, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('ReversalBurst', NULL, 'global', 1, 3, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('CountVariance', NULL, 'global', 1, 1, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('DuplicateScan', NULL, 'global', 1, 1, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('ReceivingDiscrepancy', NULL, 'global', 1, 1, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('ShippingDiscrepancy', NULL, 'global', 1, 1, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('AgeingWork', NULL, 'global', 1, 72, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('IntegrationFailure', NULL, 'global', 1, 1, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00'),
                    ('NegativeBalance', NULL, 'global', 1, 0, TRUE, FALSE, 'system', TIMESTAMPTZ '2026-09-23 00:00:00+00');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnomalyFindingHistory");

            migrationBuilder.DropTable(
                name: "AnomalyFindingObservations");

            migrationBuilder.DropTable(
                name: "AnomalyRuleConfigurations");

            migrationBuilder.DropTable(
                name: "AnomalyFindings");

            migrationBuilder.DropTable(
                name: "AnomalyDetectionRuns");
        }
    }
}

#pragma warning restore CA1861
