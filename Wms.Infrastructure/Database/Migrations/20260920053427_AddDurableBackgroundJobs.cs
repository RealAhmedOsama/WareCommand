using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableBackgroundJobs : Migration
    {
        private static readonly string[] JobExecutionIdentityColumns = ["JobName", "IdempotencyKey"];

        private static readonly string[] JobExecutionStatusColumns = ["Status", "CompletedAtUtc"];

        private static readonly string[] JobNotificationKindColumns = ["Kind", "CreatedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsJobExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Queue = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ActorUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResultSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsJobExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsJobNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeduplicationKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    JobName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    JobIdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsJobNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsJobExecutions_JobName_IdempotencyKey",
                table: "WmsJobExecutions",
                columns: JobExecutionIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsJobExecutions_Status_CompletedAtUtc",
                table: "WmsJobExecutions",
                columns: JobExecutionStatusColumns);

            migrationBuilder.CreateIndex(
                name: "IX_WmsJobNotifications_DeduplicationKey",
                table: "WmsJobNotifications",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsJobNotifications_Kind_CreatedAtUtc",
                table: "WmsJobNotifications",
                columns: JobNotificationKindColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsJobExecutions");

            migrationBuilder.DropTable(
                name: "WmsJobNotifications");
        }
    }
}
