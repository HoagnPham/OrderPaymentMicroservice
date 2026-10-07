using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Models;

namespace OrderPaymentMicroservice.Data
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            // Apply the same versioned schema used by integration and production databases.
            context.Database.Migrate();

            // Nếu đã có dữ liệu, không thêm nữa
            if (context.Products.Any())
            {
                return;
            }

            // Seed sản phẩm mẫu
            var products = new Product[]
            {
                new Product
                {
                    Name = "Cơm Tấm Sài Gòn",
                    Description = "Cơm tấm truyền thống với sườn cốt lết nướng",
                    Price = 45000,
                    Stock = 50,
                    Category = "Food",
                    ImageUrl = "/images/comtam.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Product
                {
                    Name = "Phở Bò Hà Nội",
                    Description = "Phở bò truyền thống nước dùng từ xương hầm",
                    Price = 55000,
                    Stock = 60,
                    Category = "Food",
                    ImageUrl = "/images/pho.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Product
                {
                    Name = "Bánh Mì Thịt Nướng",
                    Description = "Bánh mì giòn với thịt nướng và pâté",
                    Price = 25000,
                    Stock = 100,
                    Category = "Food",
                    ImageUrl = "/images/banhmi.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Product
                {
                    Name = "Nước Cam Ép",
                    Description = "Nước cam tươi ép tại chỗ",
                    Price = 15000,
                    Stock = 80,
                    Category = "Beverage",
                    ImageUrl = "/images/nuocam.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Product
                {
                    Name = "Cà Phê Đen Đá",
                    Description = "Cà phê đen đúc chất lượng cao",
                    Price = 12000,
                    Stock = 120,
                    Category = "Beverage",
                    ImageUrl = "/images/caphe.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Product
                {
                    Name = "Chè Ba Màu",
                    Description = "Chè ba màu đậm đà hương vị truyền thống",
                    Price = 18000,
                    Stock = 70,
                    Category = "Beverage",
                    ImageUrl = "/images/chebamau.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Product
                {
                    Name = "Dessert - Chè Chuối",
                    Description = "Chè chuối thơm mùi với nước cốt dừa",
                    Price = 20000,
                    Stock = 40,
                    Category = "Dessert",
                    ImageUrl = "/images/chechuoi.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Product
                {
                    Name = "Sâm Bố Địa",
                    Description = "Nước sâm bố địa tốt cho sức khỏe",
                    Price = 25000,
                    Stock = 50,
                    Category = "Beverage",
                    ImageUrl = "/images/sambodla.jpg",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            context.Products.AddRange(products);
            context.SaveChanges();
        }
    }
}
