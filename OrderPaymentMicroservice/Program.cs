using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using OrderPaymentMicroservice.Services.Interfaces;
using OrderPaymentMicroservice.Utilities;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure DbContext with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register services (Dependency Injection)
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<InputValidator>();
builder.Services.AddScoped<PaymentValidator>();
builder.Services.AddScoped<CreateAdminAccount>();

// Enable CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Initialize database with seed data
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    OrderPaymentMicroservice.Data.DbInitializer.Initialize(dbContext);

    // Create default admin account
    var adminAccountCreator = scope.ServiceProvider.GetRequiredService<CreateAdminAccount>();
    await adminAccountCreator.CreateDefaultAdminAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
    // Disable HTTPS redirect in Development
    // app.UseHttpsRedirection();
}
else
{
    app.UseHttpsRedirection();
}

// Enable CORS
app.UseCors("AllowAll");

// Enable static files (CSS, JS, images, etc. from wwwroot)
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

// Map default file (index.html)
app.MapFallbackToFile("index.html");

app.Run();
