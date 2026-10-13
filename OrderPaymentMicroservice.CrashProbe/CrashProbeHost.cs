using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderPaymentMicroservice.Services.Payments;

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
                builder.Services.AddSingleton<IPaymentGateway>(new BarrierGateway(inner, marker, builder.Configuration["CrashProbe:Operation"] ?? "charge"));
            }
        });
        app.Run();
    }

    private sealed class BarrierGateway(IPaymentGateway inner, string marker, string operation) : IPaymentGateway
    {
        public async Task<GatewayResult> ChargeAsync(GatewayPaymentRequest request, CancellationToken token = default)
        {
            var result = await inner.ChargeAsync(request, token);
            if(operation!="charge")return result;
            await Pause(result,token);
            return result;
        }
        private async Task Pause(GatewayResult result,CancellationToken token)
        {
            await File.WriteAllTextAsync(marker + ".tmp", result.TransactionCode, token);
            File.Move(marker + ".tmp", marker);
            // The test kills this process after the provider ledger is durable,
            // while PaymentService is still waiting and cannot finalize the DB.
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        public async Task<GatewayResult> RefundAsync(GatewayPaymentRequest r, CancellationToken t = default)
        {var result=await inner.RefundAsync(r,t);if(operation=="refund")await Pause(result,t);return result;}
        public Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest r, CancellationToken t = default) => inner.GetChargeStatusAsync(r, t);
        public Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest r, CancellationToken t = default) => inner.GetRefundStatusAsync(r, t);
    }
}
