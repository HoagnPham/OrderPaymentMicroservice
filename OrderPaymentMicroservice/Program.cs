using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using OrderPaymentMicroservice.Services.Interfaces;
using OrderPaymentMicroservice.Services.Payments;
using OrderPaymentMicroservice.Utilities;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure DbContext with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured outside source control.");
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
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.Configure<PaymentRecoveryOptions>(
    builder.Configuration.GetSection(PaymentRecoveryOptions.SectionName));
builder.Services.AddHostedService<PaymentRecoveryWorker>();
builder.Services.AddScoped<IPasswordHasher<OrderPaymentMicroservice.Models.User>, PasswordHasher<OrderPaymentMicroservice.Models.User>>();

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured outside source control and contain at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

// Enable CORS for frontend
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
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

}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
    // Disable HTTPS redirect in Development
    // app.UseHttpsRedirection();
}
else if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

// Enable CORS
app.UseCors("Frontend");

// Enable static files (CSS, JS, images, etc. from wwwroot)
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map default file (index.html)
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory-based end-to-end tests.
public partial class Program;
