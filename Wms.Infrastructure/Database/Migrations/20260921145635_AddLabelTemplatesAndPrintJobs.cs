using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelTemplatesAndPrintJobs : Migration
    {
        private static readonly string[] LabelTemplateStatusIndexColumns =
            ["Name", "DocumentType", "Language", "Format", "IsActive"];

        private static readonly string[] LabelTemplateVersionIndexColumns =
            ["Name", "Version", "Language", "WarehouseId", "CustomerCode", "SupplierCode"];

        private static readonly string[] PrintJobStatusIndexColumns =
            ["Status", "CreatedAtUtc"];

        private static readonly string[] PrintJobTemplateIndexColumns =
            ["TemplateName", "TemplateVersion", "CreatedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsLabelTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WidthMillimeters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    HeightMillimeters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    FieldsJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    BarcodesJson = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    RoutesJson = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    CustomerCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupplierCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsLabelTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsPrintJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    StationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RouteName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Transport = table.Column<int>(type: "integer", nullable: true),
                    AdapterKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    IsReprint = table.Column<bool>(type: "boolean", nullable: false),
                    ReprintReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActorUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DataJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TextPreview = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    BrowserHtml = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    PreferBrowserPdf = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsPrintJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsLabelTemplates_Name_DocumentType_Language_Format_IsActive",
                table: "WmsLabelTemplates",
                columns: LabelTemplateStatusIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_WmsLabelTemplates_Name_Version_Language_WarehouseId_Custome~",
                table: "WmsLabelTemplates",
                columns: LabelTemplateVersionIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsPrintJobs_IdempotencyKey",
                table: "WmsPrintJobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsPrintJobs_Status_CreatedAtUtc",
                table: "WmsPrintJobs",
                columns: PrintJobStatusIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_WmsPrintJobs_TemplateName_TemplateVersion_CreatedAtUtc",
                table: "WmsPrintJobs",
                columns: PrintJobTemplateIndexColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsLabelTemplates");

            migrationBuilder.DropTable(
                name: "WmsPrintJobs");
        }
    }
}
