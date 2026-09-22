using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePurchaseOrderExternalReferenceUniqueness : Migration
    {
        private static readonly string[] PurchaseOrderExternalReferenceColumns =
            ["SupplierId", "SourceType", "ExternalReference"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_SupplierId_SourceType_ExternalReference",
                table: "PurchaseOrders");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId_SourceType_ExternalReference",
                table: "PurchaseOrders",
                columns: PurchaseOrderExternalReferenceColumns,
                unique: true,
                filter: "\"ExternalReference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_SupplierId_SourceType_ExternalReference",
                table: "PurchaseOrders");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId_SourceType_ExternalReference",
                table: "PurchaseOrders",
                columns: PurchaseOrderExternalReferenceColumns);
        }
    }
}
