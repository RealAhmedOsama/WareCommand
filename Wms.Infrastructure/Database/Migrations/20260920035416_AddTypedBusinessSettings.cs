using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTypedBusinessSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsGlobalSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompanyCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefaultWarehouseId = table.Column<int>(type: "integer", nullable: true),
                    DefaultReceivingLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefaultShippingLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReceivingPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextReceivingNumber = table.Column<long>(type: "bigint", nullable: false),
                    ShippingPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextShippingNumber = table.Column<long>(type: "bigint", nullable: false),
                    AdjustmentPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextAdjustmentNumber = table.Column<long>(type: "bigint", nullable: false),
                    LowStockThreshold = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LowStockAlertLimit = table.Column<int>(type: "integer", nullable: false),
                    DashboardRefreshIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    AllowNegativeStock = table.Column<bool>(type: "boolean", nullable: false),
                    RequireLocationForAdjustment = table.Column<bool>(type: "boolean", nullable: false),
                    MaximumAdjustmentQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExpiryWarningDays = table.Column<int>(type: "integer", nullable: false),
                    BlockExpiredReceipt = table.Column<bool>(type: "boolean", nullable: false),
                    ScannerTimeoutMilliseconds = table.Column<int>(type: "integer", nullable: false),
                    MinimumBarcodeLength = table.Column<int>(type: "integer", nullable: false),
                    MaximumBarcodeLength = table.Column<int>(type: "integer", nullable: false),
                    EnableAudioFeedback = table.Column<bool>(type: "boolean", nullable: false),
                    LabelTemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LabelPaperSize = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IncludeCompanyNameOnLabels = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultReportPeriodDays = table.Column<int>(type: "integer", nullable: false),
                    MaximumReportRows = table.Column<int>(type: "integer", nullable: false),
                    DefaultLocale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultTimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IntegrationsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IntegrationEndpointUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IntegrationTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsGlobalSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsGlobalSettings_Warehouses_DefaultWarehouseId",
                        column: x => x.DefaultWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WmsWarehouseSettingsOverrides",
                columns: table => new
                {
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    DefaultReceivingLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DefaultShippingLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LowStockThreshold = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    LowStockAlertLimit = table.Column<int>(type: "integer", nullable: true),
                    DashboardRefreshIntervalSeconds = table.Column<int>(type: "integer", nullable: true),
                    ExpiryWarningDays = table.Column<int>(type: "integer", nullable: true),
                    BlockExpiredReceipt = table.Column<bool>(type: "boolean", nullable: true),
                    ScannerTimeoutMilliseconds = table.Column<int>(type: "integer", nullable: true),
                    MinimumBarcodeLength = table.Column<int>(type: "integer", nullable: true),
                    MaximumBarcodeLength = table.Column<int>(type: "integer", nullable: true),
                    EnableAudioFeedback = table.Column<bool>(type: "boolean", nullable: true),
                    DefaultReportPeriodDays = table.Column<int>(type: "integer", nullable: true),
                    MaximumReportRows = table.Column<int>(type: "integer", nullable: true),
                    Locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsWarehouseSettingsOverrides", x => x.WarehouseId);
                    table.ForeignKey(
                        name: "FK_WmsWarehouseSettingsOverrides_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsGlobalSettings_DefaultWarehouseId",
                table: "WmsGlobalSettings",
                column: "DefaultWarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsGlobalSettings");

            migrationBuilder.DropTable(
                name: "WmsWarehouseSettingsOverrides");
        }
    }
}
