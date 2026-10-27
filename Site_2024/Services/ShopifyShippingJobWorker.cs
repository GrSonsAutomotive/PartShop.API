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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var config = _options.Value;
                    if (config.AsyncShippingJobsSchemaReady &&
                        config.EnableMarketDrivenShippingAsyncWorker &&
                        string.Equals(config.ShopDomain,
                            "gr-sons-shipping-dev.myshopify.com", StringComparison.OrdinalIgnoreCase))
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var store = scope.ServiceProvider.GetRequiredService<ShopifyShippingJobStore>();
                        var processor = scope.ServiceProvider.GetRequiredService<ShopifyShippingAsyncProcessor>();
                        Guid owner = Guid.NewGuid();
                        var job = await store.ClaimAsync(owner, stoppingToken);
                        if (job != null)
                        {
                            await processor.ProcessAsync(job, owner, stoppingToken);
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
                    _log.LogError(ex, "DEV Shopify shipping background worker tick failed.");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
    }
}
