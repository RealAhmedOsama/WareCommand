using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsConnectorMappingProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectorType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ExternalIdField = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RulesJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    CultureName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ConflictPolicy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsConnectorMappingProfiles", x => x.Id);
                    table.UniqueConstraint("AK_WmsConnectorMappingProfiles_ConnectorType_Name_Version", x => new { x.ConnectorType, x.Name, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "WmsConnectorInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectorType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllowedWarehouseIdsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CredentialReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CredentialVersion = table.Column<int>(type: "integer", nullable: false),
                    ModesJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Schedule = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MappingProfileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MappingProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Cursor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    HealthStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    HealthSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRunAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulRunAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastHealthCheckAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailureCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsConnectorInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsConnectorInstances_WmsConnectorMappingProfiles_Connector~",
                        columns: x => new { x.ConnectorType, x.MappingProfileName, x.MappingProfileVersion },
                        principalTable: "WmsConnectorMappingProfiles",
                        principalColumns: new[] { "ConnectorType", "Name", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WmsConnectorExternalRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectorInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastRunId = table.Column<long>(type: "bigint", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsConnectorExternalRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsConnectorExternalRecords_WmsConnectorInstances_Connector~",
                        column: x => x.ConnectorInstanceId,
                        principalTable: "WmsConnectorInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WmsConnectorRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectorInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CursorBefore = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CursorAfter = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RecordsSeen = table.Column<int>(type: "integer", nullable: false),
                    RecordsCreated = table.Column<int>(type: "integer", nullable: false),
                    RecordsUpdated = table.Column<int>(type: "integer", nullable: false),
                    RecordsSkipped = table.Column<int>(type: "integer", nullable: false),
                    Conflicts = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsConnectorRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsConnectorRuns_WmsConnectorInstances_ConnectorInstanceId",
                        column: x => x.ConnectorInstanceId,
                        principalTable: "WmsConnectorInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorExternalRecords_ConnectorInstanceId_ExternalKey",
                table: "WmsConnectorExternalRecords",
                columns: new[] { "ConnectorInstanceId", "ExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorExternalRecords_ConnectorInstanceId_RecordType_~",
                table: "WmsConnectorExternalRecords",
                columns: new[] { "ConnectorInstanceId", "RecordType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorExternalRecords_Status_LastSeenAtUtc",
                table: "WmsConnectorExternalRecords",
                columns: new[] { "Status", "LastSeenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorInstances_ConnectorType_MappingProfileName_Mapp~",
                table: "WmsConnectorInstances",
                columns: new[] { "ConnectorType", "MappingProfileName", "MappingProfileVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorInstances_ConnectorType_Name",
                table: "WmsConnectorInstances",
                columns: new[] { "ConnectorType", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorInstances_Status_HealthStatus",
                table: "WmsConnectorInstances",
                columns: new[] { "Status", "HealthStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorMappingProfiles_ConnectorType_IsActive",
                table: "WmsConnectorMappingProfiles",
                columns: new[] { "ConnectorType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorMappingProfiles_ConnectorType_Name_Version",
                table: "WmsConnectorMappingProfiles",
                columns: new[] { "ConnectorType", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorRuns_ConnectorInstanceId_CreatedAtUtc",
                table: "WmsConnectorRuns",
                columns: new[] { "ConnectorInstanceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorRuns_ConnectorInstanceId_IdempotencyKey",
                table: "WmsConnectorRuns",
                columns: new[] { "ConnectorInstanceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsConnectorRuns_Status_CreatedAtUtc",
                table: "WmsConnectorRuns",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsConnectorExternalRecords");

            migrationBuilder.DropTable(
                name: "WmsConnectorRuns");

            migrationBuilder.DropTable(
                name: "WmsConnectorInstances");

            migrationBuilder.DropTable(
                name: "WmsConnectorMappingProfiles");
        }
    }
}
#pragma warning restore CA1861
