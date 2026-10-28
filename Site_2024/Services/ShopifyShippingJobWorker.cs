using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services
{
    // Host-owned work: survives browser navigation/closure. Jobs live in SQL,
    // not in the hosting process; a restart reclaims expired leases.
    public sealed class ShopifyShippingJobWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ShopifyShippingJobWorker> _log;
        private readonly IOptions<ShopifySettings> _options;

        public ShopifyShippingJobWorker(IServiceScopeFactory scopeFactory,
            ILogger<ShopifyShippingJobWorker> log,
            IOptions<ShopifySettings> options)
        {
            _scopeFactory = scopeFactory;
            _log = log;
            _options = options;
        }

        // Refresh the SQL lease independently of Shopify HTTP call duration.
        // Cancel pending processing if this instance can no longer prove ownership.
        private async Task KeepLeaseAliveAsync(
            ShopifyShippingJobStore store, Guid jobId, Guid owner,
            CancellationTokenSource jobCancellation, CancellationToken heartbeatStop)
        {
            while (!heartbeatStop.IsCancellationRequested &&
                   !jobCancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), heartbeatStop);
                    if (!await store.RenewLeaseAsync(jobId, owner, heartbeatStop))
                    {
                        if (!heartbeatStop.IsCancellationRequested)
                        {
                            _log.LogCritical(
                                "Lost exclusive SQL lease for shipping job {JobId}; stopping processor.",
                                jobId);
                            jobCancellation.Cancel();
                        }
                        return;
                    }
                }
                catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!heartbeatStop.IsCancellationRequested)
                    {
                        _log.LogCritical(ex,
                            "Cannot verify exclusive SQL lease for shipping job {JobId}; stopping processor.",
                            jobId);
                        jobCancellation.Cancel();
                    }
                    return;
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var config = _options.Value;
                    if (config.AsyncShippingJobsSchemaReady &&
                        config.EnableMarketDrivenShippingAsyncWorker &&
                        config.IsAsyncShippingStoreAuthorized())
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var store = scope.ServiceProvider.GetRequiredService<ShopifyShippingJobStore>();
                        var processor = scope.ServiceProvider.GetRequiredService<ShopifyShippingAsyncProcessor>();
                        Guid owner = Guid.NewGuid();
                        var job = await store.ClaimAsync(owner, stoppingToken);
                        if (job != null)
                        {
                            using var jobStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                            using var heartbeatStop = new CancellationTokenSource();
                            Task heartbeat = KeepLeaseAliveAsync(
                                store, job.JobId, owner, jobStop, heartbeatStop.Token);
                            try
                            {
                                await processor.ProcessAsync(job, owner, jobStop.Token);
                            }
                            finally
                            {
                                heartbeatStop.Cancel();
                                await heartbeat;
                            }
                            // Recheck queue promptly instead of imposing browser latency.
                            continue;
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Shopify shipping background worker tick failed.");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
    }
}
