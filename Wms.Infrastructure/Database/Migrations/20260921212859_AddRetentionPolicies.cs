using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsRetentionArchiveReferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    ArchiveLocator = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsRetentionArchiveReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsRetentionHolds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CaseReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReleaseReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsRetentionHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsRetentionPolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumRetentionDays = table.Column<int>(type: "integer", nullable: true),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    CompanyCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ArchiveBeforePurge = table.Column<bool>(type: "boolean", nullable: false),
                    ExportBeforePurge = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsRetentionPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsRetentionRuns",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    AllowDestructive = table.Column<bool>(type: "boolean", nullable: false),
                    BackupVerified = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorizationReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AsOfUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    ItemsExamined = table.Column<long>(type: "bigint", nullable: false),
                    EligibleItems = table.Column<long>(type: "bigint", nullable: false),
                    HeldItems = table.Column<long>(type: "bigint", nullable: false),
                    SkippedItems = table.Column<long>(type: "bigint", nullable: false),
                    ArchivedItems = table.Column<long>(type: "bigint", nullable: false),
                    PurgedItems = table.Column<long>(type: "bigint", nullable: false),
                    BatchCount = table.Column<int>(type: "integer", nullable: false),
                    CursorClass = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CursorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsRetentionRuns", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "WmsRetentionRunCounts",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsImplemented = table.Column<bool>(type: "boolean", nullable: false),
                    IsPurgeAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    Examined = table.Column<long>(type: "bigint", nullable: false),
                    Eligible = table.Column<long>(type: "bigint", nullable: false),
                    Held = table.Column<long>(type: "bigint", nullable: false),
                    Skipped = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsRetentionRunCounts", x => new { x.RunId, x.Class });
                    table.ForeignKey(
                        name: "FK_WmsRetentionRunCounts_WmsRetentionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "WmsRetentionRuns",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsRetentionArchiveReferences_Class_SourceType_SourceId",
                table: "WmsRetentionArchiveReferences",
                columns: new[] { "Class", "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsRetentionArchiveReferences_WarehouseId_ArchivedAtUtc",
                table: "WmsRetentionArchiveReferences",
                columns: new[] { "WarehouseId", "ArchivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsRetentionHolds_CaseReference",
                table: "WmsRetentionHolds",
                column: "CaseReference");

            migrationBuilder.CreateIndex(
                name: "IX_WmsRetentionHolds_Class_TargetType_TargetId_WarehouseId_Rel~",
                table: "WmsRetentionHolds",
                columns: new[] { "Class", "TargetType", "TargetId", "WarehouseId", "ReleasedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsRetentionPolicies_Class_WarehouseId_CompanyCode",
                table: "WmsRetentionPolicies",
                columns: new[] { "Class", "WarehouseId", "CompanyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsRetentionPolicies_PolicyKey_WarehouseId_CompanyCode",
                table: "WmsRetentionPolicies",
                columns: new[] { "PolicyKey", "WarehouseId", "CompanyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsRetentionRuns_Status_StartedAtUtc",
                table: "WmsRetentionRuns",
                columns: new[] { "Status", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsRetentionArchiveReferences");

            migrationBuilder.DropTable(
                name: "WmsRetentionHolds");

            migrationBuilder.DropTable(
                name: "WmsRetentionPolicies");

            migrationBuilder.DropTable(
                name: "WmsRetentionRunCounts");

            migrationBuilder.DropTable(
                name: "WmsRetentionRuns");
        }
    }
}
