using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMovementAdjustmentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentAfterQuantity",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentBeforeQuantity",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentDelta",
                table: "Movements",
                type: "numeric(28,12)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustmentAfterQuantity",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "AdjustmentBeforeQuantity",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "AdjustmentDelta",
                table: "Movements");
        }
    }
}
