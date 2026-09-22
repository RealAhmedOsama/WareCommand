using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkImportFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WmsBulkImportMappingProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ColumnsJson = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    CultureName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DuplicatePolicy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsBulkImportMappingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsBulkImportSourceFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "character varying(5000000)", maxLength: 5000000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsBulkImportSourceFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WmsBulkImportExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceFileId = table.Column<long>(type: "bigint", nullable: false),
                    MappingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MappingVersion = table.Column<int>(type: "integer", nullable: false),
                    CultureName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DuplicatePolicy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ValidRows = table.Column<int>(type: "integer", nullable: false),
                    InvalidRows = table.Column<int>(type: "integer", nullable: false),
                    SucceededRows = table.Column<int>(type: "integer", nullable: false),
                    FailedRows = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsBulkImportExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsBulkImportExecutions_WmsBulkImportSourceFiles_SourceFile~",
                        column: x => x.SourceFileId,
                        principalTable: "WmsBulkImportSourceFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WmsBulkImportRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ValuesJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    ValidationErrorsJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    OutputReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WmsBulkImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WmsBulkImportRows_WmsBulkImportExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "WmsBulkImportExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportExecutions_ImportType_Status_CreatedAtUtc",
                table: "WmsBulkImportExecutions",
                columns: new[] { "ImportType", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportExecutions_SourceFileId",
                table: "WmsBulkImportExecutions",
                column: "SourceFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportExecutions_UserId_IdempotencyKey",
                table: "WmsBulkImportExecutions",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportMappingProfiles_ImportType_IsActive",
                table: "WmsBulkImportMappingProfiles",
                columns: new[] { "ImportType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportMappingProfiles_ImportType_Name_Version",
                table: "WmsBulkImportMappingProfiles",
                columns: new[] { "ImportType", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportRows_ExecutionId_RowNumber",
                table: "WmsBulkImportRows",
                columns: new[] { "ExecutionId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportRows_ExecutionId_Status",
                table: "WmsBulkImportRows",
                columns: new[] { "ExecutionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WmsBulkImportSourceFiles_Sha256",
                table: "WmsBulkImportSourceFiles",
                column: "Sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WmsBulkImportMappingProfiles");

            migrationBuilder.DropTable(
                name: "WmsBulkImportRows");

            migrationBuilder.DropTable(
                name: "WmsBulkImportExecutions");

            migrationBuilder.DropTable(
                name: "WmsBulkImportSourceFiles");
        }
    }
}

#pragma warning restore CA1861
