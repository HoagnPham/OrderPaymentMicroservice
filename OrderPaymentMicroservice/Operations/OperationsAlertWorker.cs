using Microsoft.Extensions.Options;

namespace OrderPaymentMicroservice.Operations;

public sealed class OperationsAlertWorker(IServiceScopeFactory scopes, IOptions<OperationsOptions> options,
    ILogger<OperationsAlertWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if(!options.Value.AlertPollingEnabled)return;
        string previous="";
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(options.Value.AlertPollSeconds));
        do
        {
            string current;
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var status=await scope.ServiceProvider.GetRequiredService<OperationsSnapshot>().Read(token);
                current=string.Join(",",status.Alerts);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch{current="OPERATIONS_QUERY_UNAVAILABLE";}
            if(current!=previous)
            {
                if(current.Length>0)logger.LogWarning("Operational alerts: {AlertCodes}",current);
                else logger.LogInformation("Operational alerts cleared");
                previous=current;
            }
        }while(await timer.WaitForNextTickAsync(token));
    }
}
