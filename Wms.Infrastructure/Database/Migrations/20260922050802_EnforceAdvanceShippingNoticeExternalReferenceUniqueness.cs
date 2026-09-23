using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAdvanceShippingNoticeExternalReferenceUniqueness : Migration
    {
        private static readonly string[] AdvanceShippingNoticeExternalReferenceColumns =
            ["SupplierId", "SourceType", "ExternalReference"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdvanceShippingNotices_SupplierId_SourceType_ExternalRefere~",
                table: "AdvanceShippingNotices");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNotices_SupplierId_SourceType_ExternalRefere~",
                table: "AdvanceShippingNotices",
                columns: AdvanceShippingNoticeExternalReferenceColumns,
                unique: true,
                filter: "\"ExternalReference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdvanceShippingNotices_SupplierId_SourceType_ExternalRefere~",
                table: "AdvanceShippingNotices");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceShippingNotices_SupplierId_SourceType_ExternalRefere~",
                table: "AdvanceShippingNotices",
                columns: AdvanceShippingNoticeExternalReferenceColumns);
        }
    }
}
