using dataAccess.Planning.Insights;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace dataAccess.Api.Services;

/// <summary>
/// Background hosted service that periodically refreshes mentor insights
/// for all active businesses to keep cache warm.
/// </summary>
public class InsightRefreshWorker : BackgroundService
{
    private readonly ILogger<InsightRefreshWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly InsightRefreshOptions _options;

    public InsightRefreshWorker(
        ILogger<InsightRefreshWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<InsightRefreshOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "InsightRefreshWorker starting. Interval: {Interval}min, BatchSize: {BatchSize}, Enabled: {Enabled}",
            _options.RefreshIntervalMinutes,
            _options.BatchSize,
            _options.Enabled);

        if (!_options.Enabled)
        {
            _logger.LogInformation("InsightRefreshWorker is disabled via configuration");
            return;
        }

        // Wait for initial delay before first run (avoid startup collision)
        await Task.Delay(TimeSpan.FromMinutes(_options.InitialDelayMinutes), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAllBusinessesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during background insight refresh cycle");
            }

            // Wait for the configured interval before next cycle
            await Task.Delay(TimeSpan.FromMinutes(_options.RefreshIntervalMinutes), stoppingToken);
        }

        _logger.LogInformation("InsightRefreshWorker stopped");
    }

    private async Task RefreshAllBusinessesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting background insight refresh cycle");

        using var scope = _serviceProvider.CreateScope();
        var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var insightCoordinator = scope.ServiceProvider.GetRequiredService<InsightCoordinator>();

        try
        {
            // Get all distinct business IDs from suppliers table (proxy for active businesses)
            var businessIds = await appDbContext.Suppliers
                .Where(s => s.BusinessId.HasValue)
                .Select(s => s.BusinessId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} businesses to refresh", businessIds.Count);

            var successCount = 0;
            var failureCount = 0;
            var cacheHitCount = 0;

            // Process businesses in batches to avoid overwhelming the system
            for (int i = 0; i < businessIds.Count; i += _options.BatchSize)
            {
                var batch = businessIds.Skip(i).Take(_options.BatchSize).ToList();
                
                _logger.LogDebug("Processing batch {BatchNumber} ({Count} businesses)", 
                    i / _options.BatchSize + 1, batch.Count);

                foreach (var businessId in batch)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        var result = await insightCoordinator.GetOrRefreshInsightsAsync(
                            businessId, 
                            sourceEvent: "background_refresh",
                            cancellationToken: cancellationToken);

                        if (result.WasCached)
                        {
                            cacheHitCount++;
                            _logger.LogDebug("Business {BusinessId}: insights were cached", businessId);
                        }
                        else
                        {
                            successCount++;
                            _logger.LogDebug("Business {BusinessId}: insights refreshed successfully", businessId);
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        _logger.LogError(ex, "Failed to refresh insights for business {BusinessId}", businessId);
                        
                        // Continue processing other businesses
                        continue;
                    }

                    // Small delay between businesses to avoid rate limiting
                    if (_options.DelayBetweenBusinessesMs > 0)
                    {
                        await Task.Delay(_options.DelayBetweenBusinessesMs, cancellationToken);
                    }
                }

                // Delay between batches
                if (i + _options.BatchSize < businessIds.Count && _options.DelayBetweenBatchesMs > 0)
                {
                    await Task.Delay(_options.DelayBetweenBatchesMs, cancellationToken);
                }
            }

            _logger.LogInformation(
                "Background refresh cycle completed. Refreshed: {Success}, CacheHits: {CacheHits}, Failures: {Failures}",
                successCount, cacheHitCount, failureCount);

            // Emit metrics if configured (placeholder for Application Insights/CloudWatch)
            EmitMetrics(successCount, cacheHitCount, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during business refresh cycle");
            throw;
        }
    }

    private void EmitMetrics(int successCount, int cacheHitCount, int failureCount)
    {
        // TODO: Integrate with Application Insights or CloudWatch
        // For now, just structured logging that can be picked up by log aggregators
        _logger.LogInformation(
            "METRICS: InsightRefresh | Success={Success} CacheHit={CacheHit} Failure={Failure}",
            successCount, cacheHitCount, failureCount);
    }
}

/// <summary>
/// Configuration options for the InsightRefreshWorker
/// </summary>
public class InsightRefreshOptions
{
    public const string SectionName = "InsightRefresh";

    /// <summary>
    /// Whether the background refresh worker is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often to run the refresh cycle (in minutes)
    /// </summary>
    public int RefreshIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Initial delay before first refresh cycle (in minutes) to avoid startup collision
    /// </summary>
    public int InitialDelayMinutes { get; set; } = 2;

    /// <summary>
    /// Number of businesses to process in each batch
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Delay between processing individual businesses (in milliseconds)
    /// </summary>
    public int DelayBetweenBusinessesMs { get; set; } = 500;

    /// <summary>
    /// Delay between batches (in milliseconds)
    /// </summary>
    public int DelayBetweenBatchesMs { get; set; } = 2000;
}
