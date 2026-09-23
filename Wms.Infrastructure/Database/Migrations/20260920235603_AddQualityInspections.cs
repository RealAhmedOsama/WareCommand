using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QualityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    ItemCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReceiptSourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RiskLevel = table.Column<int>(type: "integer", nullable: false),
                    SamplingMethod = table.Column<int>(type: "integer", nullable: false),
                    SamplingValue = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityProfiles_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityProfiles_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityProfiles_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QualityInspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InspectionNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReceiptId = table.Column<int>(type: "integer", nullable: false),
                    ReceiptLineId = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemSkuSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    SampleBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    InspectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    AcceptedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    RejectedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    SupplierCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    QualityProfileId = table.Column<int>(type: "integer", nullable: true),
                    QualityProfileCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RiskLevel = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    LotNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDateSnapshot = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SerialNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    LicensePlateNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateIsSscc = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    InspectorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ClosureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityInspections_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspections_LicensePlates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "LicensePlates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspections_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspections_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspections_ReceiptLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspections_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspections_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspections_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QualityProfileTests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QualityProfileId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MeasurementType = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumValue = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    MaximumValue = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    AllowedValues = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfileTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityProfileTests_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QualityInspectionDispositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QualityInspectionId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    TargetInventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MovementId = table.Column<int>(type: "integer", nullable: true),
                    TargetLocationId = table.Column<int>(type: "integer", nullable: true),
                    SupervisorOverride = table.Column<bool>(type: "boolean", nullable: false),
                    OverrideReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityInspectionDispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityInspectionDispositions_Locations_TargetLocationId",
                        column: x => x.TargetLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspectionDispositions_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityInspectionDispositions_QualityInspections_QualityIns~",
                        column: x => x.QualityInspectionId,
                        principalTable: "QualityInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QualityInspectionTestResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QualityInspectionId = table.Column<int>(type: "integer", nullable: false),
                    QualityProfileTestId = table.Column<int>(type: "integer", nullable: false),
                    TestCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TestNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MeasurementType = table.Column<int>(type: "integer", nullable: false),
                    InspectedQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    RecordedValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NumericValue = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    BooleanValue = table.Column<bool>(type: "boolean", nullable: true),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttachmentReferences = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityInspectionTestResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityInspectionTestResults_QualityInspections_QualityInsp~",
                        column: x => x.QualityInspectionId,
                        principalTable: "QualityInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QualityInspectionTestResults_QualityProfileTests_QualityPro~",
                        column: x => x.QualityProfileTestId,
                        principalTable: "QualityProfileTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspectionDispositions_MovementId",
                table: "QualityInspectionDispositions",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspectionDispositions_QualityInspectionId_RecordedA~",
                table: "QualityInspectionDispositions",
                columns: new[] { "QualityInspectionId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspectionDispositions_TargetLocationId",
                table: "QualityInspectionDispositions",
                column: "TargetLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_InspectionNumber",
                table: "QualityInspections",
                column: "InspectionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_ItemId_Status",
                table: "QualityInspections",
                columns: new[] { "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_LicensePlateId",
                table: "QualityInspections",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_LotId",
                table: "QualityInspections",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_QualityProfileId",
                table: "QualityInspections",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_ReceiptId",
                table: "QualityInspections",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_ReceiptLineId_LicensePlateId",
                table: "QualityInspections",
                columns: new[] { "ReceiptLineId", "LicensePlateId" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_SupplierId",
                table: "QualityInspections",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspections_WarehouseId_Status_CreatedAt",
                table: "QualityInspections",
                columns: new[] { "WarehouseId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspectionTestResults_QualityInspectionId_QualityPro~",
                table: "QualityInspectionTestResults",
                columns: new[] { "QualityInspectionId", "QualityProfileTestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspectionTestResults_QualityProfileTestId",
                table: "QualityInspectionTestResults",
                column: "QualityProfileTestId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_Code",
                table: "QualityProfiles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_ItemId",
                table: "QualityProfiles",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_SupplierId",
                table: "QualityProfiles",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_WarehouseId_SupplierId_ItemId_ItemCategory_~",
                table: "QualityProfiles",
                columns: new[] { "WarehouseId", "SupplierId", "ItemId", "ItemCategory", "ReceiptSourceType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfileTests_QualityProfileId_Code",
                table: "QualityProfileTests",
                columns: new[] { "QualityProfileId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfileTests_QualityProfileId_Sequence",
                table: "QualityProfileTests",
                columns: new[] { "QualityProfileId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualityInspectionDispositions");

            migrationBuilder.DropTable(
                name: "QualityInspectionTestResults");

            migrationBuilder.DropTable(
                name: "QualityInspections");

            migrationBuilder.DropTable(
                name: "QualityProfileTests");

            migrationBuilder.DropTable(
                name: "QualityProfiles");
        }
    }
}
