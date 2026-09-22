using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task RegisterHashesPasswordAndLoginReturnsSignedJwt()
    {
        await using var context = CreateContext();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-signing-key-with-at-least-32-characters",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience"
        }).Build();
        var hasher = new PasswordHasher<User>();
        var service = new UserService(context, NullLogger<UserService>.Instance, hasher, configuration);

        var registered = await service.RegisterAsync(new RegisterRequest
        {
            Email = "customer@example.com",
            Password = "Correct-Horse-Battery-Staple!",
            FullName = "Customer"
        });
        var stored = await context.Users.SingleAsync();
        var login = await service.LoginAsync(new LoginRequest
        {
            Email = registered.Email,
            Password = "Correct-Horse-Battery-Staple!"
        });

        Assert.NotEqual("Correct-Horse-Battery-Staple!", stored.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(stored, stored.PasswordHash, "Correct-Horse-Battery-Staple!"));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        Assert.Equal(stored.Id.ToString(), token.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("Customer", token.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("HS256", token.Header.Alg);
    }

    [Fact]
    public async Task LegacySha256PasswordIsUpgradedAfterSuccessfulLogin()
    {
        await using var context = CreateContext();
        const string password = "Legacy-Password!";
        var legacyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        var user = new User
        {
            Email = "legacy@example.com",
            FullName = "Legacy User",
            PasswordHash = legacyHash
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var hasher = new PasswordHasher<User>();
        var service = new UserService(
            context,
            NullLogger<UserService>.Instance,
            hasher,
            CreateConfiguration());

        await service.LoginAsync(new LoginRequest { Email = user.Email, Password = password });

        Assert.NotEqual(legacyHash, user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, user.PasswordHash, password));
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-signing-key-with-at-least-32-characters",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience"
        }).Build();

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
