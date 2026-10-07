using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

public sealed class PostgresApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("order_payment_tests")
        .WithUsername("postgres")
        .WithPassword("postgres-test-password")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public IServiceProvider Services => _factory?.Services
        ?? throw new InvalidOperationException("Fixture has not been initialized.");

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _database.GetConnectionString(),
                    ["Jwt:Key"] = "integration-test-key-at-least-32-characters-long",
                    ["Jwt:Issuer"] = "OrderPaymentIntegrationTests",
                    ["Jwt:Audience"] = "OrderPaymentIntegrationTests",
                    ["PaymentRecovery:Enabled"] = "false"
                });
            });
        });

        // Force application startup here so migration/seed failures fail the fixture,
        // rather than being hidden in the first individual test.
        using var startupClient = CreateClient();
        _ = startupClient.BaseAddress;
    }

    public HttpClient CreateClient() => (_factory
        ?? throw new InvalidOperationException("Fixture has not been initialized."))
        .CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });

    public async Task DisposeAsync()
    {
        if (_factory != null)
            await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "PostgreSQL API";
}
