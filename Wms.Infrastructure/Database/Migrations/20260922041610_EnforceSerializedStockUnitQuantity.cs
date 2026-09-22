using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSerializedStockUnitQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Stock_SerialQuantity",
                table: "Stock");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Stock_SerialQuantity",
                table: "Stock",
                sql: "\"SerialNumberId\" IS NULL OR (\"QuantityAvailable\" IN (0, 1) AND \"QuantityReserved\" IN (0, 1) AND \"QuantityReserved\" <= \"QuantityAvailable\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Stock_SerialQuantity",
                table: "Stock");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Stock_SerialQuantity",
                table: "Stock",
                sql: "\"SerialNumberId\" IS NULL OR (\"QuantityAvailable\" >= 0 AND \"QuantityAvailable\" <= 1 AND \"QuantityReserved\" >= 0 AND \"QuantityReserved\" <= 1)");
        }
    }
}
