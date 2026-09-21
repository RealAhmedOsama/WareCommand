using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomersAndShipToMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TaxRegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalErpIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalChannelIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BillingAddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingAddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BillingRegion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BillingPostalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    BillingCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    DefaultCarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DefaultCarrierServiceCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    PackagingProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LabelProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AllowPartialShipment = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerItemReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    CustomerSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerBarcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerDescription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerItemReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerItemReferences_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerItemReferences_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerShipToAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DeliveryInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliveryWindowStart = table.Column<TimeSpan>(type: "time", nullable: true),
                    DeliveryWindowEnd = table.Column<TimeSpan>(type: "time", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerShipToAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerShipToAddresses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerItemReferences_CustomerId_CustomerBarcode",
                table: "CustomerItemReferences",
                columns: new[] { "CustomerId", "CustomerBarcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerItemReferences_CustomerId_CustomerSku",
                table: "CustomerItemReferences",
                columns: new[] { "CustomerId", "CustomerSku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerItemReferences_CustomerId_IsActive",
                table: "CustomerItemReferences",
                columns: new[] { "CustomerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerItemReferences_ItemId_IsActive",
                table: "CustomerItemReferences",
                columns: new[] { "ItemId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Code",
                table: "Customers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ExternalChannelIdentifier",
                table: "Customers",
                column: "ExternalChannelIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ExternalErpIdentifier",
                table: "Customers",
                column: "ExternalErpIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IsActive",
                table: "Customers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_LegalName",
                table: "Customers",
                column: "LegalName");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Priority_IsActive",
                table: "Customers",
                columns: new[] { "Priority", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerShipToAddresses_CustomerId_Code",
                table: "CustomerShipToAddresses",
                columns: new[] { "CustomerId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerShipToAddresses_CustomerId_IsActive_IsDefault",
                table: "CustomerShipToAddresses",
                columns: new[] { "CustomerId", "IsActive", "IsDefault" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerItemReferences");

            migrationBuilder.DropTable(
                name: "CustomerShipToAddresses");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
