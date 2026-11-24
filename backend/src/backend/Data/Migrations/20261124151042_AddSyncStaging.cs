using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "inventory_hash",
                table: "sync_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mode",
                table: "sync_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "full");

            migrationBuilder.AddColumn<int>(
                name: "products_rejected",
                table: "sync_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<decimal>(
                name: "stock",
                table: "products",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,4)",
                oldPrecision: 14,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "public_price",
                table: "products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.CreateTable(
                name: "sync_run_items",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    stock = table.Column<double>(type: "double precision", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    price = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_run_items", x => new { x.run_id, x.code });
                    table.ForeignKey(
                        name: "fk_sync_run_items_sync_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "sync_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_sync_runs_mode",
                table: "sync_runs",
                sql: "mode IN ('full', 'unchanged')");

            migrationBuilder.CreateIndex(
                name: "ix_sync_run_items_run_id_sequence",
                table: "sync_run_items",
                columns: new[] { "run_id", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_run_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sync_runs_mode",
                table: "sync_runs");

            migrationBuilder.DropColumn(
                name: "inventory_hash",
                table: "sync_runs");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "sync_runs");

            migrationBuilder.DropColumn(
                name: "products_rejected",
                table: "sync_runs");

            migrationBuilder.AlterColumn<decimal>(
                name: "stock",
                table: "products",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,4)",
                oldPrecision: 20,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "public_price",
                table: "products",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);
        }
    }
}
