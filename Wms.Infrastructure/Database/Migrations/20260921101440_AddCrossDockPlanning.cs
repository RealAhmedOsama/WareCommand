using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1861

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossDockPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrossDockPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    ItemCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    InboundSourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: true),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: true),
                    QuantityTolerancePercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    MinimumShelfLifeDays = table.Column<int>(type: "integer", nullable: false),
                    RequireExpiry = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPlanned = table.Column<bool>(type: "boolean", nullable: false),
                    AllowOpportunistic = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossDockPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrossDockPolicies_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPolicies_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPolicies_Locations_WarehouseId_DestinationLocation~",
                        columns: x => new { x.WarehouseId, x.DestinationLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPolicies_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPolicies_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrossDockPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreationKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ReceiptId = table.Column<int>(type: "integer", nullable: false),
                    ReceiptLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PolicyId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    ReceivedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    MatchedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    FallbackBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: true),
                    InventoryStatusId = table.Column<int>(type: "integer", nullable: false),
                    BaseUnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LicensePlateId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossDockPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrossDockPlans_CrossDockPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "CrossDockPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlans_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlans_Locations_WarehouseId_DestinationLocationId",
                        columns: x => new { x.WarehouseId, x.DestinationLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlans_Locations_WarehouseId_SourceLocationId",
                        columns: x => new { x.WarehouseId, x.SourceLocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "WarehouseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlans_ReceiptLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlans_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlans_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrossDockPlanLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CrossDockPlanId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineNumber = table.Column<int>(type: "integer", nullable: false),
                    DemandBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    MatchedBaseQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderDocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemSku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossDockPlanLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrossDockPlanLines_CrossDockPlans_CrossDockPlanId",
                        column: x => x.CrossDockPlanId,
                        principalTable: "CrossDockPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrossDockPlanLines_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlanLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlanLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDockPlanLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlanLines_CrossDockPlanId_Sequence",
                table: "CrossDockPlanLines",
                columns: new[] { "CrossDockPlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlanLines_CustomerId",
                table: "CrossDockPlanLines",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlanLines_ItemId",
                table: "CrossDockPlanLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlanLines_SalesOrderId",
                table: "CrossDockPlanLines",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlanLines_SalesOrderLineId_Status",
                table: "CrossDockPlanLines",
                columns: new[] { "SalesOrderLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_ItemId",
                table: "CrossDockPlans",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_PolicyId",
                table: "CrossDockPlans",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_ReceiptId",
                table: "CrossDockPlans",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_ReceiptLineId",
                table: "CrossDockPlans",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_WarehouseId_CreationKey",
                table: "CrossDockPlans",
                columns: new[] { "WarehouseId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_WarehouseId_DestinationLocationId",
                table: "CrossDockPlans",
                columns: new[] { "WarehouseId", "DestinationLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_WarehouseId_ReceiptLineId_Status",
                table: "CrossDockPlans",
                columns: new[] { "WarehouseId", "ReceiptLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPlans_WarehouseId_SourceLocationId",
                table: "CrossDockPlans",
                columns: new[] { "WarehouseId", "SourceLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPolicies_CustomerId",
                table: "CrossDockPolicies",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPolicies_ItemId",
                table: "CrossDockPolicies",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPolicies_SalesOrderId",
                table: "CrossDockPolicies",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPolicies_SupplierId",
                table: "CrossDockPolicies",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPolicies_WarehouseId_DestinationLocationId",
                table: "CrossDockPolicies",
                columns: new[] { "WarehouseId", "DestinationLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPolicies_WarehouseId_IsActive_EffectiveFromUtc_Eff~",
                table: "CrossDockPolicies",
                columns: new[] { "WarehouseId", "IsActive", "EffectiveFromUtc", "EffectiveToUtc", "Priority", "ItemId", "ItemCategory", "SupplierId", "CustomerId", "SalesOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDockPolicies_WarehouseId_PolicyKey",
                table: "CrossDockPolicies",
                columns: new[] { "WarehouseId", "PolicyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrossDockPlanLines");

            migrationBuilder.DropTable(
                name: "CrossDockPlans");

            migrationBuilder.DropTable(
                name: "CrossDockPolicies");
        }
    }
}

#pragma warning restore CA1861
