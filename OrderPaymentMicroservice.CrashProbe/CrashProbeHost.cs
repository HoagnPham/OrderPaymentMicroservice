using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderPaymentMicroservice.Services.Payments;
using System.Text.Json;

// Test executable only: the production API has no crash switch/endpoint.
internal static class CrashProbeHost
{
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "ledger-writer")
        {
            var gateway = new DurableFakePaymentGateway(args[1]);
            var result = gateway.ChargeAsync(new GatewayPaymentRequest(1, 2, 25m, "cross-process-key"))
                .GetAwaiter().GetResult();
            Console.WriteLine(result.TransactionCode);
            return;
        }
        var app = Program.BuildApplication(args, builder =>
        {
            var marker = builder.Configuration["CrashProbe:Marker"];
            if (!string.IsNullOrWhiteSpace(marker))
            {
                var inner = new DurableFakePaymentGateway(builder.Configuration["PaymentGateway:StoreDirectory"]!);
                builder.Services.RemoveAll<IPaymentGateway>();
                builder.Services.AddSingleton<IPaymentGateway>(new BarrierGateway(inner, marker, builder.Configuration["CrashProbe:Operation"] ?? "charge",
                    builder.Configuration["CrashProbe:ReleaseFile"],builder.Configuration["CrashProbe:CountsFile"]));
            }
        });
        app.Run();
    }

    private sealed class BarrierGateway(IPaymentGateway inner, string marker, string operation, string? releaseFile, string? countsFile) : IPaymentGateway
    {
        private readonly SemaphoreSlim _gate=new(1,1);
        private int _charge,_refund,_chargeLookup,_refundLookup;
        private async Task Count(string kind,CancellationToken token)
        {
            if(countsFile==null)return;
            await _gate.WaitAsync(token);
            try
            {
                switch(kind){case "charge":_charge++;break;case "refund":_refund++;break;case "charge_lookup":_chargeLookup++;break;case "refund_lookup":_refundLookup++;break;}
                await File.WriteAllTextAsync(countsFile+".tmp",JsonSerializer.Serialize(new{charge_calls=_charge,refund_calls=_refund,charge_status_lookups=_chargeLookup,refund_status_lookups=_refundLookup}),token);
                File.Move(countsFile+".tmp",countsFile,overwrite:true);
            }
            finally{_gate.Release();}
        }
        public async Task<GatewayResult> ChargeAsync(GatewayPaymentRequest request, CancellationToken token = default)
        {
            await Count("charge",token);
            var result = await inner.ChargeAsync(request, token);
            if(operation!="charge")return result;
            await Pause(result,token);
            return result;
        }
        private async Task Pause(GatewayResult result,CancellationToken token)
        {
            await File.WriteAllTextAsync(marker + ".tmp", result.TransactionCode, token);
            File.Move(marker + ".tmp", marker);
            // Process-kill tests never release this barrier. Reconnect tests release
            // it while DB is unavailable, so the original local finalize really fails.
            if(releaseFile==null)await Task.Delay(Timeout.InfiniteTimeSpan, token);
            else while(!File.Exists(releaseFile))await Task.Delay(25,token);
        }
        public async Task<GatewayResult> RefundAsync(GatewayPaymentRequest r, CancellationToken t = default)
        {await Count("refund",t);var result=await inner.RefundAsync(r,t);if(operation=="refund")await Pause(result,t);return result;}
        public async Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest r, CancellationToken t = default)
        {await Count("charge_lookup",t);return await inner.GetChargeStatusAsync(r,t);}
        public async Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest r, CancellationToken t = default)
        {await Count("refund_lookup",t);return await inner.GetRefundStatusAsync(r,t);}
    }
}
