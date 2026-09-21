using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseWorkEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WarehouseWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreationKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    SourceEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceEntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceLineReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    QueueCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TeamCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedTeamCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AssignedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExceptionType = table.Column<int>(type: "integer", nullable: true),
                    ExceptionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExceptionByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ExceptionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupervisorOverride = table.Column<bool>(type: "boolean", nullable: false),
                    OverrideReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseWorkCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorkCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkCommands_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseWorkLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: true),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumberId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DimensionsSnapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorkLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_InventoryStatuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "InventoryStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_Locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_SerialNumbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalTable: "SerialNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkLines_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkCommands_ExecutedAtUtc",
                table: "WarehouseWorkCommands",
                column: "ExecutedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkCommands_WarehouseWorkId_Operation_Idempotency~",
                table: "WarehouseWorkCommands",
                columns: new[] { "WarehouseWorkId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_DestinationLocationId",
                table: "WarehouseWorkLines",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_InventoryStatusId",
                table: "WarehouseWorkLines",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_ItemId_SourceLocationId_LicensePlateId",
                table: "WarehouseWorkLines",
                columns: new[] { "ItemId", "SourceLocationId", "LicensePlateId" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_LicensePlateId",
                table: "WarehouseWorkLines",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_LotId",
                table: "WarehouseWorkLines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_SerialNumberId",
                table: "WarehouseWorkLines",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_SourceLocationId",
                table: "WarehouseWorkLines",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_WarehouseId",
                table: "WarehouseWorkLines",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_WarehouseWorkId_Sequence",
                table: "WarehouseWorkLines",
                columns: new[] { "WarehouseWorkId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorks_AssignedUserId_Status",
                table: "WarehouseWorks",
                columns: new[] { "AssignedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorks_SourceEntityType_SourceEntityId_SourceLineRe~",
                table: "WarehouseWorks",
                columns: new[] { "SourceEntityType", "SourceEntityId", "SourceLineReference" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorks_WarehouseId_CreationKey",
                table: "WarehouseWorks",
                columns: new[] { "WarehouseId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorks_WarehouseId_Status_QueueCode_Priority",
                table: "WarehouseWorks",
                columns: new[] { "WarehouseId", "Status", "QueueCode", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorks_WorkNumber",
                table: "WarehouseWorks",
                column: "WorkNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WarehouseWorkCommands");

            migrationBuilder.DropTable(
                name: "WarehouseWorkLines");

            migrationBuilder.DropTable(
                name: "WarehouseWorks");
        }
    }
}
