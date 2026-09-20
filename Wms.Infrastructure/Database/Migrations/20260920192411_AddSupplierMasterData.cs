using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierMasterData : Migration
    {
        private static readonly string[] SupplierItemReferenceItemIdIsActiveColumns = ["ItemId", "IsActive"];
        private static readonly string[] SupplierItemReferenceSupplierIdIsActiveColumns = ["SupplierId", "IsActive"];
        private static readonly string[] SupplierItemReferenceSupplierIdVendorBarcodeColumns = ["SupplierId", "VendorBarcode"];
        private static readonly string[] SupplierItemReferenceSupplierIdVendorSkuColumns = ["SupplierId", "VendorSku"];
        private static readonly string[] SupplierPreferredWarehouseIdIsActiveColumns = ["PreferredWarehouseId", "IsActive"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TaxRegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalErpIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StateOrProvince = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PreferredWarehouseId = table.Column<int>(type: "integer", nullable: true),
                    PreferredDockLocationId = table.Column<int>(type: "integer", nullable: true),
                    DefaultLeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    OverDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    UnderDeliveryTolerancePercent = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    RequiresLot = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresExpiry = table.Column<bool>(type: "boolean", nullable: false),
                    QualityProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LabelRule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefaultCurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suppliers_Locations_PreferredDockLocationId",
                        column: x => x.PreferredDockLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Suppliers_Warehouses_PreferredWarehouseId",
                        column: x => x.PreferredWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierItemReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemPackagingId = table.Column<int>(type: "integer", nullable: true),
                    VendorSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VendorBarcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VendorPackaging = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UnitsPerPurchasePackage = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    MinimumOrderQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierItemReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierItemReferences_ItemPackagings_ItemPackagingId",
                        column: x => x.ItemPackagingId,
                        principalTable: "ItemPackagings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierItemReferences_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierItemReferences_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItemReferences_ItemId_IsActive",
                table: "SupplierItemReferences",
                columns: SupplierItemReferenceItemIdIsActiveColumns);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItemReferences_ItemPackagingId",
                table: "SupplierItemReferences",
                column: "ItemPackagingId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItemReferences_SupplierId_IsActive",
                table: "SupplierItemReferences",
                columns: SupplierItemReferenceSupplierIdIsActiveColumns);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItemReferences_SupplierId_VendorBarcode",
                table: "SupplierItemReferences",
                columns: SupplierItemReferenceSupplierIdVendorBarcodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItemReferences_SupplierId_VendorSku",
                table: "SupplierItemReferences",
                columns: SupplierItemReferenceSupplierIdVendorSkuColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_ExternalErpIdentifier",
                table: "Suppliers",
                column: "ExternalErpIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_IsActive",
                table: "Suppliers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_LegalName",
                table: "Suppliers",
                column: "LegalName");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_PreferredDockLocationId",
                table: "Suppliers",
                column: "PreferredDockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_PreferredWarehouseId_IsActive",
                table: "Suppliers",
                columns: SupplierPreferredWarehouseIdIsActiveColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierItemReferences");

            migrationBuilder.DropTable(
                name: "Suppliers");
        }
    }
}
