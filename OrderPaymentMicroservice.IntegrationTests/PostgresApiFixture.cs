using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

public sealed class PostgresApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private LocalPostgresServer? _localDatabase;
    private WebApplicationFactory<Program>? _factory;

    public string ConnectionString { get; private set; } = string.Empty;

    public IServiceProvider Services => _factory?.Services
        ?? throw new InvalidOperationException("Fixture has not been initialized.");

    public async Task InitializeAsync()
    {
        string connectionString;
        if (DockerFactAttribute.IsDockerAvailableForTests)
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("order_payment_tests")
                .WithUsername("postgres")
                .WithPassword("postgres-test-password")
                .Build();
            await _container.StartAsync();
            connectionString = _container.GetConnectionString();
        }
        else
        {
            _localDatabase = await LocalPostgresServer.StartAsync();
            connectionString = _localDatabase.ConnectionString;
        }

        ConnectionString = connectionString;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // WebApplication.CreateBuilder reads these values before the later
            // ConfigureAppConfiguration callback is applied by the test host.
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Jwt:Key", "integration-test-key-at-least-32-characters-long");
            builder.UseSetting("Jwt:Issuer", "OrderPaymentIntegrationTests");
            builder.UseSetting("Jwt:Audience", "OrderPaymentIntegrationTests");
            builder.UseSetting("PaymentRecovery:Enabled", "false");
            builder.UseSetting("PaymentGateway:Mode", "MemoryFake");
            builder.UseSetting("ApiAdmission:AuthenticationPermitLimit", "10000");
            builder.UseSetting("ApiAdmission:ApiPermitLimit", "10000");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["Jwt:Key"] = "integration-test-key-at-least-32-characters-long",
                    ["Jwt:Issuer"] = "OrderPaymentIntegrationTests",
                    ["Jwt:Audience"] = "OrderPaymentIntegrationTests",
                    ["PaymentRecovery:Enabled"] = "false",
                    ["PaymentGateway:Mode"] = "MemoryFake",
                    ["ApiAdmission:AuthenticationPermitLimit"] = "10000",
                    ["ApiAdmission:ApiPermitLimit"] = "10000"
                });
            });
        });

        // Force application startup here so migration/seed failures fail the fixture,
        // rather than being hidden in the first individual test.
        using var startupClient = CreateClient();
        _ = startupClient.BaseAddress;
    }

    public WebApplicationFactory<Program> CreateIsolatedFactory(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null) =>
        (_factory ?? throw new InvalidOperationException("Fixture not initialized")).WithWebHostBuilder(builder =>
        {
            foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            if (services != null) builder.ConfigureServices(services);
        });

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
        if (_container != null)
            await _container.DisposeAsync();
        if (_localDatabase != null)
            await _localDatabase.DisposeAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "PostgreSQL API";
}
