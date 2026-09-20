using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardMovementLimitSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecentMovementLimit",
                table: "WmsWarehouseSettingsOverrides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecentMovementLimit",
                table: "WmsGlobalSettings",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecentMovementLimit",
                table: "WmsWarehouseSettingsOverrides");

            migrationBuilder.DropColumn(
                name: "RecentMovementLimit",
                table: "WmsGlobalSettings");
        }
    }
}
