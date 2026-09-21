using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkforceAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WarehouseWorkActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseWorkId = table.Column<int>(type: "integer", nullable: false),
                    WorkerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorkActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkActivities_WarehouseWorks_WarehouseWorkId",
                        column: x => x.WarehouseWorkId,
                        principalTable: "WarehouseWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkActivities_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseWorkerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    TeamCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShiftCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShiftStartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShiftEndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SkillCodesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CertificationCodesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PreferredZoneLocationIdsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorkerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkerProfiles_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseWorkQueues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkType = table.Column<int>(type: "integer", nullable: false),
                    ZoneLocationId = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    RequiredTeamCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AssignmentStrategy = table.Column<int>(type: "integer", nullable: false),
                    RequiredSkillCodesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RequiredCertificationCodesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorkQueues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkQueues_Locations_WarehouseId_ZoneLocationId",
                        columns: x => new { x.WarehouseId, x.ZoneLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkQueues_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkActivities_WarehouseId_WarehouseWorkId_Started~",
                table: "WarehouseWorkActivities",
                columns: new[] { "WarehouseId", "WarehouseWorkId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkActivities_WarehouseId_WorkerUserId_Category_S~",
                table: "WarehouseWorkActivities",
                columns: new[] { "WarehouseId", "WorkerUserId", "Category", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkActivities_WarehouseWorkId",
                table: "WarehouseWorkActivities",
                column: "WarehouseWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkerProfiles_UserId_WarehouseId",
                table: "WarehouseWorkerProfiles",
                columns: new[] { "UserId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkerProfiles_WarehouseId_IsActive_TeamCode",
                table: "WarehouseWorkerProfiles",
                columns: new[] { "WarehouseId", "IsActive", "TeamCode" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkerProfiles_WarehouseId_ShiftStartAtUtc_ShiftEn~",
                table: "WarehouseWorkerProfiles",
                columns: new[] { "WarehouseId", "ShiftStartAtUtc", "ShiftEndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkQueues_WarehouseId_Code",
                table: "WarehouseWorkQueues",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkQueues_WarehouseId_IsActive_WorkType_Priority",
                table: "WarehouseWorkQueues",
                columns: new[] { "WarehouseId", "IsActive", "WorkType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkQueues_WarehouseId_ZoneLocationId_IsActive",
                table: "WarehouseWorkQueues",
                columns: new[] { "WarehouseId", "ZoneLocationId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WarehouseWorkActivities");

            migrationBuilder.DropTable(
                name: "WarehouseWorkerProfiles");

            migrationBuilder.DropTable(
                name: "WarehouseWorkQueues");
        }
    }
}

#pragma warning restore CA1861
