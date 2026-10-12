using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace OrderPaymentMicroservice.Migrations;

public partial class EnforceInventoryAndQuantityBounds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.AddCheckConstraint(name: "CK_Products_Stock_Nonnegative", table: "Products", sql: "\"Stock\" >= 0");
            migrationBuilder.AddCheckConstraint(name: "CK_Products_Price_Positive", table: "Products", sql: "\"Price\" > 0");
            migrationBuilder.AddCheckConstraint(name: "CK_CartItems_Quantity_Positive", table: "CartItems", sql: "\"Quantity\" > 0");
            migrationBuilder.AddCheckConstraint(name: "CK_OrderItems_Quantity_Positive", table: "OrderItems", sql: "\"Quantity\" > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.DropCheckConstraint(name: "CK_Products_Stock_Nonnegative", table: "Products");
            migrationBuilder.DropCheckConstraint(name: "CK_Products_Price_Positive", table: "Products");
            migrationBuilder.DropCheckConstraint(name: "CK_CartItems_Quantity_Positive", table: "CartItems");
            migrationBuilder.DropCheckConstraint(name: "CK_OrderItems_Quantity_Positive", table: "OrderItems");
    }
}
