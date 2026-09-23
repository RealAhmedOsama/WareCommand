using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkInterleaving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WarehouseWorkInterleavingPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PriorityWeight = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    DeadlineWeight = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TravelWeight = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ZoneAffinityWeight = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    MaximumTravelMinutes = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    AllowCrossWorkType = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorkInterleavingPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkInterleavingPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseWorkRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    FromLocationId = table.Column<int>(type: "integer", nullable: false),
                    ToLocationId = table.Column<int>(type: "integer", nullable: false),
                    RouteCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TravelMinutes = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    DistanceMeters = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseWorkRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkRoutes_Locations_WarehouseId_FromLocationId",
                        columns: x => new { x.WarehouseId, x.FromLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkRoutes_Locations_WarehouseId_ToLocationId",
                        columns: x => new { x.WarehouseId, x.ToLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseWorkRoutes_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkInterleavingPolicies_WarehouseId_Code",
                table: "WarehouseWorkInterleavingPolicies",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkInterleavingPolicies_WarehouseId_IsActive",
                table: "WarehouseWorkInterleavingPolicies",
                columns: new[] { "WarehouseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkRoutes_WarehouseId_FromLocationId",
                table: "WarehouseWorkRoutes",
                columns: new[] { "WarehouseId", "FromLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkRoutes_WarehouseId_RouteCode_FromLocationId_To~",
                table: "WarehouseWorkRoutes",
                columns: new[] { "WarehouseId", "RouteCode", "FromLocationId", "ToLocationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkRoutes_WarehouseId_RouteCode_IsActive",
                table: "WarehouseWorkRoutes",
                columns: new[] { "WarehouseId", "RouteCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkRoutes_WarehouseId_ToLocationId",
                table: "WarehouseWorkRoutes",
                columns: new[] { "WarehouseId", "ToLocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WarehouseWorkInterleavingPolicies");

            migrationBuilder.DropTable(
                name: "WarehouseWorkRoutes");
        }
    }
}
#pragma warning restore CA1861
