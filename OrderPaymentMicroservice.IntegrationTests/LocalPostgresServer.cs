using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace OrderPaymentMicroservice.IntegrationTests;

/// <summary>
/// User-owned temporary PostgreSQL cluster used when Docker is unavailable.
/// The cluster lives under the OS temp directory and is removed after tests.
/// </summary>
internal sealed class LocalPostgresServer : IAsyncDisposable
{
    private const string DatabaseName = "order_payment_tests";
    private readonly string _root;
    private readonly string _data;
    private readonly int _port;
    private bool _started;

    private LocalPostgresServer(string root, int port)
    {
        _root = root;
        _data = Path.Combine(root, "data");
        _port = port;
    }

    private static string? BinDirectory => FindBinDirectory();

    public static bool IsAvailable => BinDirectory != null;

    public string ConnectionString =>
        $"Host=127.0.0.1;Port={_port};Database={DatabaseName};Username={Environment.UserName};Pooling=false";

    public static async Task<LocalPostgresServer> StartAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orderpayment-postgres-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var server = new LocalPostgresServer(root, ReservePort());
        try
        {
            await server.InitializeAsync();
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    private async Task InitializeAsync()
    {
        var socketDirectory = Path.Combine(_root, "socket");
        Directory.CreateDirectory(socketDirectory);
        await RunAsync("initdb", "-D", _data, "-A", "trust", "--no-locale", "--encoding=UTF8");
        await RestartAsync();
        await RunAsync("createdb", "-h", "127.0.0.1", "-p", _port.ToString(), DatabaseName);
    }

    public async Task RestartAsync()
    {
        if (_started) return;
        var socketDirectory = Path.Combine(_root, "socket");
        await RunAsync(
            "pg_ctl", "-D", _data, "-w", "start", "-l", Path.Combine(_root, "postgres.log"), "-o",
            $"-h 127.0.0.1 -p {_port} -k {socketDirectory}");
        _started = true;
    }

    public async Task StopAsync()
    {
        if (!_started) return;
        await RunAsync("pg_ctl", "-D", _data, "-w", "stop", "-m", "fast");
        _started = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            try
            {
                await StopAsync();
            }
            catch
            {
                // Cleanup is best-effort; retain the original test exception.
            }
            _started = false;
        }

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task RunAsync(string executable, params string[] arguments)
    {
        var bin = BinDirectory ?? throw new InvalidOperationException("PostgreSQL binaries were not found");
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(bin, executable),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start PostgreSQL command {executable}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"PostgreSQL command {executable} failed with exit code {process.ExitCode}: {output}{error}");
        }
    }

    internal static string? FindBinDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("POSTGRES_BIN");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "postgres")))
            return configured;

        const string root = "/usr/lib/postgresql";
        if (!Directory.Exists(root))
            return null;
        return Directory.GetDirectories(root)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Select(path => Path.Combine(path, "bin"))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "postgres")));
    }
}
