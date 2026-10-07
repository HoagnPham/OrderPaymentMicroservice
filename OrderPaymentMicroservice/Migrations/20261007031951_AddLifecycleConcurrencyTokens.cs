using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderPaymentMicroservice.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecycleConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Orders");
        }
    }
}
