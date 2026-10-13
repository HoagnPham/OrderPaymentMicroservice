using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class ProcessRestartTests(PostgresApiFixture fixture)
{
    [DockerFact]
    public async Task Killed_API_recovers_committed_gateway_charge_without_charging_or_releasing_again()
    {
        var root = Path.Combine(Path.GetTempPath(), "restart-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "charged.marker");
        var ledger = Path.Combine(root, "ledger");
        Process? process = null;
        Task<HttpResponseMessage>? pending = null;
        try
        {
            var port = ReservePort();
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            process = Start(port, ledger, marker);
            await Ready(client, process);
            var email = $"restart-{Guid.NewGuid():N}@example.test";
            const string password = "Restart-Password!123";
            var registered = await client.PostAsJsonAsync("api/users/register", new RegisterRequest { Email = email, Password = password, FullName = "Restart Probe" });
            registered.EnsureSuccessStatusCode();
            var loginResponse = await client.PostAsJsonAsync("api/users/login", new LoginRequest { Email = email, Password = password });
            loginResponse.EnsureSuccessStatusCode();
            var login = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            var product = (await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
            var created = await client.PostAsJsonAsync("api/orders", new CreateOrderRequest
            { TotalAmount = product.Price, Items = [new() { ProductId = product.Id, Quantity = 1, UnitPrice = product.Price }] });
            created.EnsureSuccessStatusCode();
            var order = (await created.Content.ReadFromJsonAsync<OrderResponse>())!;
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/payments")
            { Content = JsonContent.Create(new CreatePaymentRequest { OrderId = order.Id, Amount = order.TotalAmount }) };
            request.Headers.Add("Idempotency-Key", "restart-key-" + order.Id);
            pending = client.SendAsync(request);
            await WaitUntil(() => Task.FromResult(File.Exists(marker)), process);
            var transaction = await File.ReadAllTextAsync(marker);
            Assert.Single(Directory.GetFiles(ledger, "*.json"));
            var journalBefore = await File.ReadAllBytesAsync(Directory.GetFiles(ledger, "*.json").Single());
            int paymentId;
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
                paymentId = payment.Id;
                Assert.Equal(PaymentStatus.PROCESSING, payment.Status);
                Assert.Equal(OrderStatus.PAYMENT_PENDING, (await db.Orders.FindAsync(order.Id))!.Status);
                Assert.Equal(product.Stock - 1, (await db.Products.FindAsync(product.Id))!.Stock);
                // Worker selects stale records; set it before the second OS process starts.
                payment.UpdatedAt = DateTime.UtcNow.AddMinutes(-5);
                await db.SaveChangesAsync();
            }
            var firstPid = process.Id;
            await Stop(process); process.Dispose(); process = null;
            try { using var interrupted = await pending; } catch (HttpRequestException) { } catch (TaskCanceledException) { }
            process = Start(port, ledger, marker: null);
            Assert.NotEqual(firstPid, process.Id);
            await Ready(client, process);
            await WaitUntil(async () =>
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.Payments.AnyAsync(p => p.Id == paymentId && p.Status == PaymentStatus.SUCCESS)
                    && await db.AuditEvents.AnyAsync(e => e.ActorKind == "SYSTEM" && e.ResourceId == paymentId && e.Phase == "RESULT" && e.Outcome == "SUCCESS");
            }, process);
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var payment = await db.Payments.SingleAsync(p => p.Id == paymentId);
                var persisted = await db.Orders.SingleAsync(o => o.Id == order.Id);
                Assert.Equal(transaction, payment.TransactionCode);
                Assert.Equal(1, payment.AttemptCount);
                Assert.Equal(OrderStatus.PAID, persisted.Status);
                Assert.Equal(InventoryReservationStatus.RESERVED, persisted.InventoryStatus);
                Assert.Equal(product.Stock - 1, (await db.Products.FindAsync(product.Id))!.Stock);
                Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == order.Id));
            }
            Assert.Single(Directory.GetFiles(ledger, "*.json"));
            Assert.Equal(journalBefore, await File.ReadAllBytesAsync(Directory.GetFiles(ledger, "*.json").Single()));
            var evidenceDirectory = Path.Combine(RepositoryRoot(), "security-analysis/results");
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "process_restart.results.json"), JsonSerializer.Serialize(new
            {
                passed = true, run_at_utc = DateTime.UtcNow, first_pid = firstPid, restarted_pid = process.Id,
                kill_window = "Gateway journal published; API payment still PROCESSING before finalize",
                recovered_by = "PaymentRecoveryWorker in restarted OS process", charge_records = 1,
                attempt_count = 1, inventory_released = false, journal_unchanged = true,
                limitations = new[] { "Local durable simulator on one filesystem; no real money or provider callback verification.", "Abrupt API process kill, not power-loss/disk failure or distributed storage." }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (process != null) { await Stop(process); process.Dispose(); }
            if (pending != null) { try { using var response = await pending; } catch { } }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [DockerFact]
    public async Task Separate_processes_replay_one_durable_gateway_result()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ledger-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var dll = Path.Combine(RepositoryRoot(), "OrderPaymentMicroservice.CrashProbe", "bin", configuration, "net10.0", "OrderPaymentMicroservice.CrashProbe.dll");
            async Task<string> Write()
            {
                var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                info.ArgumentList.Add(dll); info.ArgumentList.Add("ledger-writer"); info.ArgumentList.Add(directory);
                using var process = Process.Start(info)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
                finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
                Assert.True(process.ExitCode == 0, await error);
                return (await output).Trim();
            }
            var results = await Task.WhenAll(Write(), Write());
            Assert.StartsWith("DURABLE-FAKE-charge-", results[0]);
            Assert.Equal(results[0], results[1]);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private Process Start(int port, string ledger, string? marker)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var project = Path.Combine(RepositoryRoot(), "OrderPaymentMicroservice.CrashProbe");
        var dll = Path.Combine(project, "bin", configuration, "net10.0", "OrderPaymentMicroservice.CrashProbe.dll");
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = project, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(dll);
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        info.Environment["ConnectionStrings__DefaultConnection"] = fixture.ConnectionString;
        info.Environment["Jwt__Key"] = "restart-test-key-at-least-32-characters-long";
        info.Environment["Jwt__Issuer"] = "RestartTests"; info.Environment["Jwt__Audience"] = "RestartTests";
        info.Environment["PaymentGateway__Mode"] = "DurableFake";
        info.Environment["PaymentGateway__StoreDirectory"] = ledger;
        info.Environment["PaymentRecovery__Enabled"] = marker == null ? "true" : "false";
        info.Environment["PaymentRecovery__StaleAfterSeconds"] = "5";
        info.Environment["PaymentRecovery__ScanIntervalSeconds"] = "5";
        if (marker != null) info.Environment["CrashProbe__Marker"] = marker;
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }

    private static async Task Stop(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static async Task Ready(HttpClient client, Process process) => await WaitUntil(async () =>
    { try { using var r = await client.GetAsync("api/products"); return r.IsSuccessStatusCode; } catch (HttpRequestException) { return false; } }, process);
    private static async Task WaitUntil(Func<Task<bool>> condition, Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException($"API probe exited with {process.ExitCode}");
            if (await condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("API restart probe timed out");
    }
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OrderPaymentMicroservice.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found");
    }
    private static int ReservePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
}
