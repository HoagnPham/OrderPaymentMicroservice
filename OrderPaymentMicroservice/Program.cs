using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using OrderPaymentMicroservice.Auditing;
using System.Text;
using Microsoft.Extensions.Options;
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

var app = Program.BuildApplication(args);
app.Run();

public partial class Program
{
    public static WebApplication BuildApplication(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.

        // Configure DbContext with PostgreSQL
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured outside source control.");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Register services (Dependency Injection)
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IOrderService, OrderService>();
        builder.Services.AddScoped<IPaymentService, PaymentService>();
        builder.Services.AddScoped<IProductService, ProductService>();
        builder.Services.AddScoped<ICartService, CartService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<InputValidator>();
        builder.Services.AddScoped<PaymentValidator>();
        var gatewayMode = builder.Configuration["PaymentGateway:Mode"] ?? "DurableFake";
        IPaymentGateway gateway;
        if (gatewayMode == "MemoryFake")
            gateway = new FakePaymentGateway();
        else if (gatewayMode == "DurableFake")
        {
            var directory = builder.Configuration["PaymentGateway:StoreDirectory"] ?? "App_Data/payment-gateway";
            gateway = new DurableFakePaymentGateway(
                Path.IsPathRooted(directory) ? directory : Path.Combine(builder.Environment.ContentRootPath, directory));
        }
        else
            throw new InvalidOperationException("PaymentGateway:Mode must be DurableFake or MemoryFake");
        builder.Services.AddOptions<GatewayCallTimeoutOptions>()
            .Bind(builder.Configuration.GetSection("PaymentGateway"))
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<IPaymentGateway>(provider => new TimeoutPaymentGateway(gateway,
            TimeSpan.FromMilliseconds(provider.GetRequiredService<IOptions<GatewayCallTimeoutOptions>>().Value.RequestTimeoutMilliseconds)));
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
                          .AllowAnyHeader()
                              .WithExposedHeaders("X-Correlation-Id", "Retry-After");
                }
            });
        });

        builder.Services.AddOptions<ApiAdmissionOptions>().Bind(builder.Configuration.GetSection("ApiAdmission"))
            .ValidateDataAnnotations().ValidateOnStart();
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize =
            builder.Configuration.GetValue<long>("ApiAdmission:MaxBodyBytes", 65536));
        builder.Services.AddSingleton<IAuditWriter, AuditWriter>();
        builder.Services.AddRateLimiter(limits =>
        {
            var admission = builder.Configuration.GetSection("ApiAdmission").Get<ApiAdmissionOptions>() ?? new();
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter("static");
                var endpoint = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                var auth = endpoint?.ControllerName == "Users" && endpoint.ActionName is "Login" or "Register";
                var identity = auth ? null : context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var key = auth ? "auth-ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown")
                    : identity != null ? "user:" + identity : "api-ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = auth ? admission.AuthenticationPermitLimit : admission.ApiPermitLimit,
                    Window = TimeSpan.FromSeconds(admission.WindowSeconds), QueueLimit = 0, AutoReplenishment = true });
            });
            limits.OnRejected = async (rejected, token) =>
            {
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    rejected.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                rejected.HttpContext.Response.ContentType = "application/json";
                await rejected.HttpContext.Response.WriteAsync("{\"message\":\"Too many requests. Please wait before trying again.\"}", token);
            };
        });
        builder.Services.AddControllers();
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        // Add logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        configure?.Invoke(builder);
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

        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<RequestBodyLimitMiddleware>();
        app.UseMiddleware<AuditTrailMiddleware>();
        app.UseAuthorization();

        app.MapControllers();

        // Map default file (index.html)
        app.MapFallbackToFile("index.html");

        return app;
    }
}
