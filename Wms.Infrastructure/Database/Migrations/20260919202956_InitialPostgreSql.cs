using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        private static readonly string[] LocationActivePickableIndexColumns = ["IsActive", "IsPickable"];
        private static readonly string[] LocationActiveReceivableIndexColumns = ["IsActive", "IsReceivable"];
        private static readonly string[] LotItemNumberIndexColumns = ["ItemId", "Number"];
        private static readonly string[] StockIdentityIndexColumns = ["ItemId", "LocationId", "LotId", "SerialNumber"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresLot = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresSerial = table.Column<bool>(type: "boolean", nullable: false),
                    ShelfLifeDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemBarcodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemBarcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemBarcodes_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ManufacturedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lots_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ParentLocationId = table.Column<int>(type: "integer", nullable: true),
                    IsPickable = table.Column<bool>(type: "boolean", nullable: false),
                    IsReceivable = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Locations_ParentLocationId",
                        column: x => x.ParentLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    FromLocationId = table.Column<int>(type: "integer", nullable: true),
                    ToLocationId = table.Column<int>(type: "integer", nullable: true),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movements_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stock",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    QuantityAvailable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    QuantityReserved = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stock_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stock_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stock_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemBarcodes_ItemId",
                table: "ItemBarcodes",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_IsActive",
                table: "Items",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Sku",
                table: "Items",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Code",
                table: "Locations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_IsActive_IsPickable",
                table: "Locations",
                columns: LocationActivePickableIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_IsActive_IsReceivable",
                table: "Locations",
                columns: LocationActiveReceivableIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_ParentLocationId",
                table: "Locations",
                column: "ParentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_WarehouseId",
                table: "Locations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ExpiryDate",
                table: "Lots",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_IsActive",
                table: "Lots",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ItemId_Number",
                table: "Lots",
                columns: LotItemNumberIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movements_FromLocationId",
                table: "Movements",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ItemId",
                table: "Movements",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_LotId",
                table: "Movements",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ReferenceNumber",
                table: "Movements",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_Timestamp",
                table: "Movements",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ToLocationId",
                table: "Movements",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_Type",
                table: "Movements",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_UserId",
                table: "Movements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId",
                table: "Stock",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ItemId_LocationId_LotId_SerialNumber",
                table: "Stock",
                columns: StockIdentityIndexColumns,
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_LocationId",
                table: "Stock",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_LotId",
                table: "Stock",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsActive",
                table: "Warehouses",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemBarcodes");

            migrationBuilder.DropTable(
                name: "Movements");

            migrationBuilder.DropTable(
                name: "Stock");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Lots");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "Items");
        }
    }
}
