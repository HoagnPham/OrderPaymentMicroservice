using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using OrderPaymentMicroservice.DTOs.Users;

namespace OrderPaymentMicroservice.IntegrationTests;

internal sealed class OperationalProbe : IAsyncDisposable
{
    private readonly Process _process;
    public HttpClient Client { get; }
    public int Pid => _process.Id;
    private OperationalProbe(Process process, int port)
    { _process=process;Client=new(){BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(30)}; }

    public static async Task<OperationalProbe> Start(string connection, string ledger, bool recovery=false, string? marker=null, int permits=100000, string operation="charge")
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var project=Path.Combine(Root,"OrderPaymentMicroservice.CrashProbe");
        var info=new ProcessStartInfo("dotnet"){WorkingDirectory=project,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(Path.Combine(project,"bin",configuration,"net10.0","OrderPaymentMicroservice.CrashProbe.dll"));
        foreach(var pair in new Dictionary<string,string>
        {
            ["ASPNETCORE_ENVIRONMENT"]="Testing",["ASPNETCORE_URLS"]=$"http://127.0.0.1:{port}",
            ["ConnectionStrings__DefaultConnection"]=connection,["Jwt__Key"]="operational-test-key-at-least-32-characters-long",
            ["Jwt__Issuer"]="OperationalTests",["Jwt__Audience"]="OperationalTests",
            ["PaymentGateway__Mode"]="DurableFake",["PaymentGateway__StoreDirectory"]=ledger,
            ["PaymentRecovery__Enabled"]=recovery.ToString(),["PaymentRecovery__StaleAfterSeconds"]="5",
            ["PaymentRecovery__ScanIntervalSeconds"]="5",["ApiAdmission__ApiPermitLimit"]=permits.ToString(),
            ["ApiAdmission__AuthenticationPermitLimit"]="10000",
            ["ApiAdmission__WindowSeconds"]="60",["Logging__LogLevel__Default"]="Warning"
        }) info.Environment[pair.Key]=pair.Value;
        if(marker!=null)info.Environment["CrashProbe__Marker"]=marker;
        else info.Environment.Remove("CrashProbe__Marker");
        info.Environment["CrashProbe__Operation"]=operation;
        var process=Process.Start(info)!;
        process.OutputDataReceived+=(_,_)=>{};process.ErrorDataReceived+=(_,_)=>{};
        process.BeginOutputReadLine();process.BeginErrorReadLine();
        var host=new OperationalProbe(process,port);
        try { await host.Wait(async()=>{try{using var r=await host.Client.GetAsync("api/products");return r.IsSuccessStatusCode;}catch(HttpRequestException){return false;}});return host; }
        catch {await host.DisposeAsync();throw;}
    }

    public async Task Wait(Func<Task<bool>> condition)
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(30))
        { if(_process.HasExited)throw new InvalidOperationException("Operational probe exited: "+_process.ExitCode);if(await condition())return;await Task.Delay(100); }
        throw new TimeoutException("Operational probe deadline exceeded");
    }
    public async Task<LoginResponse> Account()
    {
        var email=$"operations-{Guid.NewGuid():N}@example.test";
        using var registered=await Client.PostAsJsonAsync("api/users/register",new RegisterRequest{Email=email,Password="Operations-Password!123",FullName="Operations Test"});registered.EnsureSuccessStatusCode();
        using var logged=await Client.PostAsJsonAsync("api/users/login",new LoginRequest{Email=email,Password="Operations-Password!123"});logged.EnsureSuccessStatusCode();
        var user=(await logged.Content.ReadFromJsonAsync<LoginResponse>())!;Authorize(user.Token);return user;
    }
    public void Authorize(string token)=>Client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if(!_process.HasExited){_process.Kill(true);await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));}
        _process.Dispose();
    }
    public static string Root
    {
        get {var d=new DirectoryInfo(AppContext.BaseDirectory);while(d!=null&&!File.Exists(Path.Combine(d.FullName,"OrderPaymentMicroservice.slnx")))d=d.Parent;return d?.FullName??throw new InvalidOperationException("Repository not found");}
    }
    public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    public static async Task Evidence(string name, object evidence)=>await File.WriteAllTextAsync(Path.Combine(Root,"security-analysis/results",name),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    public static Dictionary<string,string> SourceHashes(params string[] paths)=>paths.ToDictionary(p=>p,p=>Hash(File.ReadAllBytes(Path.Combine(Root,p))));
}

public sealed class LocalPostgresFactAttribute: Xunit.FactAttribute
{
    public LocalPostgresFactAttribute(){if(!LocalPostgresServer.IsAvailable)Skip="Local PostgreSQL server/client tools required for operational drill.";}
}
