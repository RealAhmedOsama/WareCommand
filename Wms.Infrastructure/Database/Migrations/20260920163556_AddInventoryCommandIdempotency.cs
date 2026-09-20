using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCommandIdempotency : Migration
    {
        private static readonly string[] CallerCommandColumns = ["CallerScope", "CommandKey"];
        private static readonly string[] OperationCreatedColumns = ["OperationType", "CreatedAt"];
        private static readonly string[] StatusExpiryColumns = ["Status", "ExpiresAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryCommandIdempotencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommandKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CallerScope = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResultPayloadJson = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: true),
                    ResultReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCommandIdempotencies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCommandIdempotencies_CallerScope_CommandKey",
                table: "InventoryCommandIdempotencies",
                columns: CallerCommandColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCommandIdempotencies_OperationType_CreatedAt",
                table: "InventoryCommandIdempotencies",
                columns: OperationCreatedColumns);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCommandIdempotencies_Status_ExpiresAtUtc",
                table: "InventoryCommandIdempotencies",
                columns: StatusExpiryColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryCommandIdempotencies");
        }
    }
}
