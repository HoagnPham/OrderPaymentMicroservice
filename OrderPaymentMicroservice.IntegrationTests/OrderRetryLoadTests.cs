using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class OrderRetryLoadTests
{
    private sealed record LogicalSample(int Status,int Attempts,double ElapsedMs,int? OrderId,int[] AttemptStatuses,double[] AttemptElapsedMs,int[] DelaysMs);

    [LocalPostgresFact]
    public async Task CompareBoundedSameKeyRetryWithOneShotAcrossUsersAndProducts()
    {
        await using var postgres=await LocalPostgresServer.StartAsync();
        var root=Path.Combine(Path.GetTempPath(),"retry-comparison-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var clients=new List<HttpClient>();
        AppDbContext Database()=>new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        try
        {
            await using var first=await OperationalProbe.Start(postgres.ConnectionString,Path.Combine(root,"ledger"));
            await using var second=await OperationalProbe.Start(postgres.ConnectionString,Path.Combine(root,"ledger"));
            for(var i=0;i<8;i++)
            {
                var host=i%2==0?first:second;var user=await host.Account();
                var client=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(10)};
                client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",user.Token);clients.Add(client);
            }
            async Task<(object Summary,LogicalSample[] Samples)> Run(int maxAttempts,string label)
            {
                List<int> ids;
                await using(var db=Database())
                {
                    var products=Enumerable.Range(0,4).Select(i=>new Product{Name=$"Retry comparison {label} {i}",Price=10,Stock=1000}).ToArray();
                    db.Products.AddRange(products);await db.SaveChangesAsync();ids=products.Select(p=>p.Id).ToList();
                }
                using var gate=new SemaphoreSlim(8);var elapsed=Stopwatch.StartNew();
                var samples=await Task.WhenAll(Enumerable.Range(0,96).Select(async i=>
                {
                    await gate.WaitAsync();
                    try
                    {
                        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        var timer=Stopwatch.StartNew();var random=new Random(20261013+i);
                        var key=$"load-retry-{label}-{Guid.NewGuid():N}";
                        var payload=JsonSerializer.Serialize(new CreateOrderRequest{TotalAmount=10,Items=[new(){ProductId=ids[(i+i/8)%4],Quantity=1,UnitPrice=10}]});
                        var statuses=new List<int>();var latencies=new List<double>();var delays=new List<int>();
                        for(var attempt=0;attempt<maxAttempts;attempt++)
                        {
                            var requestTimer=Stopwatch.StartNew();
                            using var message=new HttpRequestMessage(HttpMethod.Post,"api/orders"){Content=new StringContent(payload,System.Text.Encoding.UTF8,"application/json")};message.Headers.Add("Idempotency-Key",key);
                            using var response=await clients[i%8].SendAsync(message,budget.Token);var body=await response.Content.ReadAsStringAsync(budget.Token);
                            statuses.Add((int)response.StatusCode);latencies.Add(requestTimer.Elapsed.TotalMilliseconds);
                            Assert.True((int)response.StatusCode is 201 or 409,response.StatusCode.ToString());
                            if((int)response.StatusCode==201)
                            {var order=JsonSerializer.Deserialize<OrderResponse>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;return new LogicalSample(201,attempt+1,timer.Elapsed.TotalMilliseconds,order.Id,statuses.ToArray(),latencies.ToArray(),delays.ToArray());}
                            using var error=JsonDocument.Parse(body);
                            if(attempt+1==maxAttempts||error.RootElement.GetProperty("code").GetString()!="ORDER_CONCURRENT_WRITE"||!error.RootElement.GetProperty("retryable").GetBoolean())
                                return new LogicalSample(409,attempt+1,timer.Elapsed.TotalMilliseconds,null,statuses.ToArray(),latencies.ToArray(),delays.ToArray());
                            var ceiling=200*(1<<attempt);var delay=(int)(ceiling/2+random.NextDouble()*ceiling/2);delays.Add(delay);await Task.Delay(delay,budget.Token);
                        }
                        throw new InvalidOperationException("Retry budget invariant failed");
                    }
                    finally{gate.Release();}
                }));
                elapsed.Stop();var created=samples.Count(s=>s.Status==201);Assert.True(created>0);
                Assert.Equal(created,samples.Where(s=>s.OrderId.HasValue).Select(s=>s.OrderId).Distinct().Count());
                Assert.All(samples,s=>Assert.InRange(s.Attempts,1,maxAttempts));
                await using(var db=Database())
                {
                    Assert.Equal(created,await db.Orders.CountAsync(o=>o.OrderItems.Any(item=>ids.Contains(item.ProductId))));
                    var stocks=await db.Products.Where(p=>ids.Contains(p.Id)).Select(p=>p.Stock).ToArrayAsync();Assert.All(stocks,s=>Assert.True(s>=0));Assert.Equal(4000-created,stocks.Sum());
                    var orderIds=samples.Where(s=>s.OrderId.HasValue).Select(s=>s.OrderId!.Value).ToList();Assert.Equal(created,await db.AuditEvents.CountAsync(e=>e.Phase=="COMMIT"&&e.Action=="ORDER_INSERT"&&orderIds.Contains(e.ResourceId!.Value)));
                }
                var times=samples.Select(s=>s.ElapsedMs).Order().ToArray();double P(double p)=>times[(int)Math.Ceiling(p*times.Length)-1];
                var attempts=samples.Sum(s=>s.Attempts);
                return (new{logical_requests=96,concurrency=8,users=8,products=4,max_attempts=maxAttempts,duration_seconds=elapsed.Elapsed.TotalSeconds,
                    http_attempts=attempts,retries=attempts-96,created_orders=created,success_rate=(double)created/96,
                    logical_requests_per_second=96/elapsed.Elapsed.TotalSeconds,successful_orders_per_second=created/elapsed.Elapsed.TotalSeconds,
                    http_attempts_per_second=attempts/elapsed.Elapsed.TotalSeconds,p50_ms=P(.5),p95_ms=P(.95),p99_ms=P(.99),
                    final_status_counts=samples.GroupBy(s=>s.Status).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
                    attempt_status_counts=samples.SelectMany(s=>s.AttemptStatuses).GroupBy(s=>s).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
                    samples=samples.Select(s=>new{status=s.Status,attempts=s.Attempts,elapsed_ms=s.ElapsedMs,attempt_statuses=s.AttemptStatuses,attempt_elapsed_ms=s.AttemptElapsedMs,delays_ms=s.DelaysMs}),
                    latency_definition="Logical request after semaphore through final body, including backoff; excludes queue. Nearest-rank percentiles."},samples);
            }
            var baseline=await Run(1,"one-shot");var retried=await Run(4,"bounded-retry");
            await OperationalProbe.Evidence("order_retry_load.results.json",new{passed=true,run_at_utc=DateTime.UtcNow,api_processes=2,baseline=baseline.Summary,bounded_retry=retried.Summary,jitter_seed=20261013,
                source_sha256=OperationalProbe.SourceHashes("OrderPaymentMicroservice.IntegrationTests/OrderRetryLoadTests.cs","OrderPaymentMicroservice.IntegrationTests/OperationalProbe.cs","OrderPaymentMicroservice/Controllers/OrdersController.cs","OrderPaymentMicroservice/Services/OrderService.cs","OrderPaymentMicroservice/Data/PersistenceConflicts.cs","OrderPaymentMicroservice/wwwroot/js/app.js"),
                limitations=new[]{"One local comparison; baseline runs first and scenarios use distinct product sets. No statistical significance or production SLA claim.","C# HTTP driver mirrors browser four-attempt/8s budget and jitter; browser helper is verified separately with mocked fetch.","API/auth quotas raised to 100000/10000; no payment backlog, network/503 retry or distributed quota proof.","No speed/success improvement assertion: safety and bounded attempts are assertions; timing and success ratios are observed."}});
        }
        finally{foreach(var client in clients)client.Dispose();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
