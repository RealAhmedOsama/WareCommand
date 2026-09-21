using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationLinksToWarehouseWorkLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReservationAllocationId",
                table: "WarehouseWorkLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReservationId",
                table: "WarehouseWorkLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseWorkLines_ReservationId_ReservationAllocationId",
                table: "WarehouseWorkLines",
                columns: new[] { "ReservationId", "ReservationAllocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehouseWorkLines_ReservationId_ReservationAllocationId",
                table: "WarehouseWorkLines");

            migrationBuilder.DropColumn(
                name: "ReservationAllocationId",
                table: "WarehouseWorkLines");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "WarehouseWorkLines");
        }
    }
}

#pragma warning restore CA1861
