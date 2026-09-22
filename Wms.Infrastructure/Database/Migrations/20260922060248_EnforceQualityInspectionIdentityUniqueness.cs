using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EnforceQualityInspectionIdentityUniqueness : Migration
    {
        private static readonly string[] ReceiptLineIdLicensePlateIdColumns = ["ReceiptLineId", "LicensePlateId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QualityInspections_ReceiptLineId_LicensePlateId",
                table: "QualityInspections");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_ReceiptLineId",
                table: "QualityInspections",
                column: "ReceiptLineId",
                unique: true,
                filter: "\"LicensePlateId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_ReceiptLineId_LicensePlateId",
                table: "QualityInspections",
                columns: ReceiptLineIdLicensePlateIdColumns,
                unique: true,
                filter: "\"LicensePlateId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QualityInspections_ReceiptLineId",
                table: "QualityInspections");

            migrationBuilder.DropIndex(
                name: "IX_QualityInspections_ReceiptLineId_LicensePlateId",
                table: "QualityInspections");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_ReceiptLineId_LicensePlateId",
                table: "QualityInspections",
                columns: ReceiptLineIdLicensePlateIdColumns);
        }
    }
}
