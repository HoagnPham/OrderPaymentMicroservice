using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderPaymentMicroservice.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableOrderPaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_OrderId_IdempotencyKey",
                table: "Payments");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GatewayMessage",
                table: "Payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundIdempotencyKey",
                table: "Payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundTransactionCode",
                table: "Payments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryStatus",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"Payments\" SET \"UpdatedAt\" = \"CreatedAt\" WHERE \"UpdatedAt\" IS NULL;");
            migrationBuilder.Sql("UPDATE \"Orders\" SET \"UpdatedAt\" = \"CreatedAt\" WHERE \"UpdatedAt\" IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RefundIdempotencyKey",
                table: "Payments",
                column: "RefundIdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_OrderId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_RefundIdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "GatewayMessage",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundIdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundTransactionCode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "InventoryStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId_IdempotencyKey",
                table: "Payments",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true);
        }
    }
}
