using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddB2bDocumentExchange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsB2bMappingProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Standard = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RulesJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsB2bMappingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsTradingPartners",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Standard = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CredentialReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CredentialVersion = table.Column<int>(type: "integer", nullable: false),
                    AllowedWarehouseIdsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DocumentTypesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    MappingProfileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MappingProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    RequireAcknowledgement = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsTradingPartners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsB2bDocuments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TradingPartnerId = table.Column<long>(type: "bigint", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Direction = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TransportMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InterchangeControlNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GroupControlNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DocumentControlNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalIdentityKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LineCount = table.Column<int>(type: "integer", nullable: false),
                    DeclaredLineCount = table.Column<int>(type: "integer", nullable: true),
                    ValidationErrorsJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    AcknowledgementStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReplayCount = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsB2bDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsB2bDocuments_WmsTradingPartners_TradingPartnerId",
                        column: x => x.TradingPartnerId,
                        principalTable: "WmsTradingPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WmsB2bAcknowledgements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    AcknowledgementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ControlNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReasonMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsB2bAcknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsB2bAcknowledgements_WmsB2bDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "WmsB2bDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bAcknowledgements_ControlNumber",
                table: "WmsB2bAcknowledgements",
                column: "ControlNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bAcknowledgements_DocumentId_AcknowledgementType",
                table: "WmsB2bAcknowledgements",
                columns: new[] { "DocumentId", "AcknowledgementType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bDocuments_Status_CreatedAtUtc",
                table: "WmsB2bDocuments",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bDocuments_TradingPartnerId_ExternalIdentityKey",
                table: "WmsB2bDocuments",
                columns: new[] { "TradingPartnerId", "ExternalIdentityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bDocuments_TradingPartnerId_IdempotencyKey",
                table: "WmsB2bDocuments",
                columns: new[] { "TradingPartnerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bDocuments_TradingPartnerId_InterchangeControlNumber_D~",
                table: "WmsB2bDocuments",
                columns: new[] { "TradingPartnerId", "InterchangeControlNumber", "DocumentControlNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bDocuments_TradingPartnerId_MessageId",
                table: "WmsB2bDocuments",
                columns: new[] { "TradingPartnerId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bDocuments_WarehouseId_CreatedAtUtc",
                table: "WmsB2bDocuments",
                columns: new[] { "WarehouseId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsB2bMappingProfiles_DocumentType_Name_Version",
                table: "WmsB2bMappingProfiles",
                columns: new[] { "DocumentType", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsTradingPartners_Code",
                table: "WmsTradingPartners",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsB2bAcknowledgements");

            migrationBuilder.DropTable(
                name: "WmsB2bMappingProfiles");

            migrationBuilder.DropTable(
                name: "WmsB2bDocuments");

            migrationBuilder.DropTable(
                name: "WmsTradingPartners");
        }
    }
}
#pragma warning restore CA1861
