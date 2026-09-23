using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLotLifecycleManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HoldUntil",
                table: "Lots",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Lots",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecallReason",
                table: "Lots",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecalledAt",
                table: "Lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetestDate",
                table: "Lots",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Lots",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SupplierLotNumber",
                table: "Lots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoldUntil",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "RecallReason",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "RecalledAt",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "RetestDate",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "SupplierLotNumber",
                table: "Lots");
        }
    }
}
