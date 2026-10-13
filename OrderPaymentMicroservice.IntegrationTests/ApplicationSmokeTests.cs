using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class ApplicationSmokeTests
{
    [LocalPostgresFact]
    public async Task NormalApplicationServesShopAndCompletesRealBrowserFlow()
    {
        await using var postgres=await LocalPostgresServer.StartAsync();
        var root=Path.Combine(Path.GetTempPath(),"application-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            await using var host=await OperationalProbe.Start(postgres.ConnectionString,Path.Combine(root,"ledger"),realApplication:true);
            using var live=await host.Client.GetAsync("health/live");live.EnsureSuccessStatusCode();
            using var ready=await host.Client.GetAsync("health/ready");ready.EnsureSuccessStatusCode();
            using var page=await host.Client.GetAsync("");page.EnsureSuccessStatusCode();Assert.Contains("checkoutForm",await page.Content.ReadAsStringAsync());
            using var script=await host.Client.GetAsync("js/app.js");script.EnsureSuccessStatusCode();Assert.Contains("createOrderWithRetry",await script.Content.ReadAsStringAsync());
            var info=new ProcessStartInfo("python3"){WorkingDirectory=OperationalProbe.Root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add("security-analysis/browser/live_smoke.py");info.ArgumentList.Add(host.Client.BaseAddress!.ToString());
            using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));}finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
            Assert.True(process.ExitCode==0,(await output)+(await error));
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
            Assert.Equal(1,await db.Orders.CountAsync());Assert.Equal(1,await db.Payments.CountAsync());
            Assert.True(await db.Payments.AllAsync(p=>p.Status==OrderPaymentMicroservice.Models.PaymentStatus.SUCCESS));
            Assert.True(await db.AuditEvents.AnyAsync(e=>e.Phase=="COMMIT"&&e.ResourceType=="PAYMENT"));
            Assert.Single(Directory.GetFiles(Path.Combine(root,"ledger"),"*.json"));
            var evidencePath=Path.Combine(OperationalProbe.Root,"security-analysis/results/application_smoke.results.json");
            var evidence=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(await File.ReadAllTextAsync(evidencePath))!;
            evidence["database_assertions"]=JsonSerializer.SerializeToElement(new{orders=1,payments=1,payment_status="SUCCESS",journal_entries=1,commit_audit_present=true});
            evidence["source_sha256"]=JsonSerializer.SerializeToElement(OperationalProbe.SourceHashes("OrderPaymentMicroservice.IntegrationTests/ApplicationSmokeTests.cs","OrderPaymentMicroservice.IntegrationTests/OperationalProbe.cs","security-analysis/browser/live_smoke.py","security-analysis/browser/live_smoke.js","OrderPaymentMicroservice/wwwroot/js/app.js","OrderPaymentMicroservice/wwwroot/index.html","OrderPaymentMicroservice/Program.cs"));
            await File.WriteAllTextAsync(evidencePath,JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
