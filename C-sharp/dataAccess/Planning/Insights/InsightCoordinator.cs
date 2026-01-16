using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Contracts;
using dataAccess.Entities;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dataAccess.Planning.Insights
{
    /// <summary>
    /// Orchestrates the insight generation pipeline: scanning, formatting, and caching.
    /// Implements change detection to avoid redundant processing.
    /// </summary>
    public sealed class InsightCoordinator
    {
        private readonly InsightScanner _scanner;
        private readonly BusinessMentorFormatter _formatter;
        private readonly AppDbContext _appDb;
        private readonly AiDbContext _aiDb;
        private readonly ILogger<InsightCoordinator> _logger;

        private const int CACHE_FRESHNESS_MINUTES = 30; // Consider insights fresh for 30 minutes

        public InsightCoordinator(
            InsightScanner scanner,
            BusinessMentorFormatter formatter,
            AppDbContext appDb,
            AiDbContext aiDb,
            ILogger<InsightCoordinator> logger)
        {
            _scanner = scanner;
            _formatter = formatter;
            _appDb = appDb;
            _aiDb = aiDb;
            _logger = logger;
        }

        /// <summary>
        /// Gets the latest insights for a business, using cache if fresh, otherwise rescanning.
        /// </summary>
        public async Task<InsightCoordinatorResult> GetLatestInsightsAsync(
            int businessId,
            string sourceEvent = "manual",
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            
            _logger.LogInformation(
                "[InsightCoordinator] GetLatestInsights | BusinessId: {BusinessId}, ForceRefresh: {ForceRefresh}, Source: {SourceEvent}",
                businessId, forceRefresh, sourceEvent);

            try
            {
                // Check if we should use cached insights
                if (!forceRefresh)
                {
                    var cachedResult = await TryGetCachedInsightsAsync(businessId, ct);
                    if (cachedResult != null)
                    {
                        sw.Stop();
                        _logger.LogInformation(
                            "[InsightCoordinator] ✅ CACHE_HIT | BusinessId: {BusinessId}, Age: {Age}min, Duration: {Duration}ms",
                            businessId, cachedResult.CacheAgeMinutes, sw.ElapsedMilliseconds);
                        
                        return cachedResult;
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[InsightCoordinator] ❌ CACHE_MISS | BusinessId: {BusinessId}",
                            businessId);
                    }
                }

                // Cache miss or force refresh - run full pipeline
                _logger.LogInformation(
                    "[InsightCoordinator] 🔄 REFRESH_START | BusinessId: {BusinessId}, Reason: {Reason}",
                    businessId, forceRefresh ? "forced" : "cache_expired");

                var result = await RefreshInsightsAsync(businessId, sourceEvent, ct);
                
                sw.Stop();
                result.TotalDurationMs = sw.ElapsedMilliseconds;
                
                _logger.LogInformation(
                    "[InsightCoordinator] ✅ REFRESH_COMPLETE | BusinessId: {BusinessId}, Duration: {Duration}ms, Insights: {Count}, Scan: {ScanMs}ms",
                    businessId, sw.ElapsedMilliseconds, result.Insights.Count, result.ScanDurationMs);

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "[InsightCoordinator] ❌ FAILURE | BusinessId: {BusinessId}, Duration: {Duration}ms",
                    businessId, sw.ElapsedMilliseconds);

                // Fallback: try to return last successful insights
                return await GetFallbackInsightsAsync(businessId, ex.Message, ct);
            }
        }

        /// <summary>
        /// Forces a fresh scan and persists new insights.
        /// Alias method for background worker to explicitly track cache status.
        /// </summary>
        public async Task<InsightCoordinatorResult> GetOrRefreshInsightsAsync(
            int businessId,
            string sourceEvent = "background_refresh",
            CancellationToken cancellationToken = default)
        {
            return await GetLatestInsightsAsync(
                businessId, 
                sourceEvent, 
                forceRefresh: false, 
                ct: cancellationToken);
        }

        /// <summary>
        /// Forces a fresh scan and persists new insights.
        /// </summary>
        public async Task<InsightCoordinatorResult> RefreshInsightsAsync(
            int businessId,
            string sourceEvent = "manual",
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "[InsightCoordinator] RefreshInsights | BusinessId: {BusinessId}, Source: {SourceEvent}",
                businessId, sourceEvent);

            // Step 1: Run detectors
            var scanResult = await _scanner.ScanAllAsync(businessId, ct);

            if (!scanResult.Success)
            {
                throw new Exception($"Scanner failed: {scanResult.ErrorMessage}");
            }

            // Step 2: Format findings with Gemini
            var formattedInsights = await _formatter.FormatInsightsAsync(scanResult, ct);

            // Step 3: Persist to database
            var savedInsights = await PersistInsightsAsync(
                businessId,
                scanResult,
                formattedInsights,
                sourceEvent,
                ct);

            _logger.LogInformation(
                "[InsightCoordinator] Persisted {Count} insights to database | BusinessId: {BusinessId}",
                savedInsights.Count, businessId);

            return new InsightCoordinatorResult
            {
                BusinessId = businessId,
                Insights = savedInsights,
                ScanDurationMs = scanResult.DurationMs,
                DetectorVersion = scanResult.DetectorVersion,
                CacheHit = false,
                Success = true
            };
        }

        /// <summary>
        /// Attempts to retrieve fresh cached insights.
        /// </summary>
        private async Task<InsightCoordinatorResult?> TryGetCachedInsightsAsync(
            int businessId,
            CancellationToken ct)
        {
            // Get latest insights from database
            var cachedInsights = await _aiDb.AiInsights
                .Where(ai => ai.BusinessId == businessId)
                .OrderByDescending(ai => ai.CreatedAt)
                .Take(10) // Get recent insights across all categories
                .ToListAsync(ct);

            if (!cachedInsights.Any())
            {
                _logger.LogDebug("[InsightCoordinator] No cached insights found | BusinessId: {BusinessId}", businessId);
                return null;
            }

            // Check if cache is fresh enough
            var latestInsight = cachedInsights.First();
            var age = DateTime.UtcNow - latestInsight.CreatedAt;

            if (age.TotalMinutes > CACHE_FRESHNESS_MINUTES)
            {
                _logger.LogDebug(
                    "[InsightCoordinator] Cache expired | BusinessId: {BusinessId}, Age: {Age}min",
                    businessId, age.TotalMinutes);
                return null;
            }

            // Check if business data has changed since last scan
            var hasDataChanged = await HasBusinessDataChangedAsync(businessId, latestInsight.CreatedAt, ct);
            if (hasDataChanged)
            {
                _logger.LogDebug(
                    "[InsightCoordinator] Business data changed since cache | BusinessId: {BusinessId}",
                    businessId);
                return null;
            }

            _logger.LogDebug(
                "[InsightCoordinator] 💾 CACHE_VALID | BusinessId: {BusinessId}, Age: {Age}min, Count: {Count}",
                businessId, age.TotalMinutes, cachedInsights.Count);

            return new InsightCoordinatorResult
            {
                BusinessId = businessId,
                Insights = cachedInsights,
                DetectorVersion = latestInsight.DetectorVersion,
                CacheHit = true,
                Success = true,
                CacheAgeMinutes = (int)age.TotalMinutes,
                WasCached = true
            };
        }

        /// <summary>
        /// Checks if business data has changed since the last insight run.
        /// Sprint 4: Now uses activitylog table for real data change detection.
        /// </summary>
        private async Task<bool> HasBusinessDataChangedAsync(
            int businessId,
            DateTime sinceTimestamp,
            CancellationToken ct)
        {
            try
            {
                // Check if any activity logged after 'sinceTimestamp'
                var hasRecentActivity = await _appDb.ActivityLogs
                    .AnyAsync(log => 
                        log.BusinessId == businessId && 
                        log.CreatedAt > sinceTimestamp,
                        ct);

                _logger.LogDebug(
                    "[InsightCoordinator] Activity check | BusinessId: {BusinessId}, Since: {Since}, HasActivity: {HasActivity}",
                    businessId, sinceTimestamp, hasRecentActivity);

                return hasRecentActivity;
            }
            catch (Exception ex)
            {
                // Fallback: If activitylog query fails, invalidate cache to be safe
                _logger.LogWarning(ex,
                    "[InsightCoordinator] ActivityLog query failed, invalidating cache | BusinessId: {BusinessId}",
                    businessId);
                return true;
            }
        }

        /// <summary>
        /// Persists formatted insights to the database.
        /// </summary>
        private async Task<List<AiInsight>> PersistInsightsAsync(
            int businessId,
            InsightScanResult scanResult,
            Dictionary<string, string> formattedInsights,
            string sourceEvent,
            CancellationToken ct)
        {
            var savedInsights = new List<AiInsight>();
            var now = DateTime.UtcNow;

            foreach (var (category, mentorText) in formattedInsights)
            {
                // Get findings for this category
                var categoryFindings = scanResult.Findings
                    .Where(f => f.Category == category)
                    .ToList();

                var insight = new AiInsight
                {
                    Id = Guid.NewGuid(),
                    BusinessId = businessId,
                    Category = category,
                    DetectorVersion = scanResult.DetectorVersion,
                    RawFindings = JsonSerializer.Serialize(categoryFindings),
                    AiSummary = mentorText,
                    CreatedAt = now,
                    SourceEvent = sourceEvent
                };

                _aiDb.AiInsights.Add(insight);
                savedInsights.Add(insight);
            }

            await _aiDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[InsightCoordinator] Saved {Count} insights | BusinessId: {BusinessId}",
                savedInsights.Count, businessId);

            return savedInsights;
        }

        /// <summary>
        /// Returns last successful insights as fallback when current run fails.
        /// </summary>
        private async Task<InsightCoordinatorResult> GetFallbackInsightsAsync(
            int businessId,
            string errorMessage,
            CancellationToken ct)
        {
            _logger.LogWarning(
                "[InsightCoordinator] Attempting fallback | BusinessId: {BusinessId}",
                businessId);

            var fallbackInsights = await _aiDb.AiInsights
                .Where(ai => ai.BusinessId == businessId)
                .OrderByDescending(ai => ai.CreatedAt)
                .Take(5)
                .ToListAsync(ct);

            if (fallbackInsights.Any())
            {
                _logger.LogInformation(
                    "[InsightCoordinator] Fallback successful | BusinessId: {BusinessId}, Count: {Count}",
                    businessId, fallbackInsights.Count);

                return new InsightCoordinatorResult
                {
                    BusinessId = businessId,
                    Insights = fallbackInsights,
                    DetectorVersion = fallbackInsights.First().DetectorVersion,
                    CacheHit = true,
                    Success = false,
                    ErrorMessage = $"Using cached insights due to error: {errorMessage}"
                };
            }

            _logger.LogError(
                "[InsightCoordinator] Fallback failed - no cached insights | BusinessId: {BusinessId}",
                businessId);

            return new InsightCoordinatorResult
            {
                BusinessId = businessId,
                Insights = new List<AiInsight>(),
                Success = false,
                ErrorMessage = errorMessage
            };
        }
        /// <summary>
        /// Gets Morning Briefing insight (top 3 priorities).
        /// Sprint 2: Now checks cache first to avoid redundant API calls.
        /// </summary>
        public async Task<string> GetMorningBriefingAsync(
            int businessId,
            CancellationToken ct = default)
        {
            _logger.LogInformation("[InsightCoordinator] GetMorningBriefing | Checking cache | BusinessId: {BusinessId}", businessId);

            // Check cache first
            var cached = await _aiDb.AiInsights
                .Where(i => i.BusinessId == businessId 
                         && i.Category == "morning-briefing"
                         && i.CreatedAt > DateTime.UtcNow.AddMinutes(-CACHE_FRESHNESS_MINUTES))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync(ct);
            
            if (cached != null)
            {
                var cacheAge = (DateTime.UtcNow - cached.CreatedAt).TotalMinutes;
                _logger.LogInformation(
                    "[InsightCoordinator] ✅ CACHE_HIT | Category: morning-briefing, Age: {Age:F1}min",
                    cacheAge);
                return cached.AiSummary;
            }
            
            // Cache miss - generate fresh insight
            _logger.LogInformation("[InsightCoordinator] ⚠️ CACHE_MISS | Category: morning-briefing | Generating fresh insight");
            
            var issues = await _scanner.ScanTop3PrioritiesAsync(businessId, ct);
            var briefing = await _formatter.FormatMorningBriefingAsync(issues, ct);

            // Save to cache for future requests
            var insight = new AiInsight
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                Category = "morning-briefing",
                DetectorVersion = "v1.0.0",
                RawFindings = JsonSerializer.Serialize(issues),
                AiSummary = briefing,
                CreatedAt = DateTime.UtcNow,
                SourceEvent = "on-demand-dashboard"
            };
            _aiDb.AiInsights.Add(insight);
            await _aiDb.SaveChangesAsync(ct);
            
            _logger.LogInformation("[InsightCoordinator] 💾 CACHED | Category: morning-briefing");

            return briefing;
        }

        /// <summary>
        /// Gets Inventory Risk insight for a specific product.
        /// Sprint 2: Now checks cache first to avoid redundant API calls.
        /// </summary>
        public async Task<string> GetInventoryRiskAsync(
            int businessId,
            int productId,
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "[InsightCoordinator] GetInventoryRisk | Checking cache | BusinessId: {BusinessId}, ProductId: {ProductId}",
                businessId, productId);

            // Check cache first
            var cached = await _aiDb.AiInsights
                .Where(i => i.BusinessId == businessId 
                         && i.Category == "inventory-risk"
                         && i.CreatedAt > DateTime.UtcNow.AddMinutes(-CACHE_FRESHNESS_MINUTES))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync(ct);
            
            if (cached != null)
            {
                var cacheAge = (DateTime.UtcNow - cached.CreatedAt).TotalMinutes;
                _logger.LogInformation(
                    "[InsightCoordinator] ✅ CACHE_HIT | Category: inventory-risk, Age: {Age:F1}min",
                    cacheAge);
                return cached.AiSummary;
            }
            
            // Cache miss - generate fresh insight
            _logger.LogInformation("[InsightCoordinator] ⚠️ CACHE_MISS | Category: inventory-risk | Generating fresh insight");
            
            var riskData = await _scanner.ScanProductRiskAsync(businessId, productId, ct);
            
            if (riskData == null)
            {
                return "No inventory risk data available for this product.";
            }

            var analysis = await _formatter.FormatInventoryRiskAsync(riskData, ct);

            // Save to cache for future requests
            var insight = new AiInsight
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                Category = "inventory-risk",
                DetectorVersion = "v1.0.0",
                RawFindings = JsonSerializer.Serialize(riskData),
                AiSummary = analysis,
                CreatedAt = DateTime.UtcNow,
                SourceEvent = "on-demand-inventory"
            };
            _aiDb.AiInsights.Add(insight);
            await _aiDb.SaveChangesAsync(ct);
            
            _logger.LogInformation("[InsightCoordinator] 💾 CACHED | Category: inventory-risk");

            return analysis;
        }

        /// <summary>
        /// Gets Profit Teaching insight (revenue vs profit margins).
        /// Sprint 2: Now checks cache first to avoid redundant API calls.
        /// </summary>
        public async Task<string> GetProfitTeachingAsync(
            int businessId,
            int periodDays = 90,
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "[InsightCoordinator] GetProfitTeaching | Checking cache | BusinessId: {BusinessId}, PeriodDays: {PeriodDays}",
                businessId, periodDays);

            // Check cache first
            var cached = await _aiDb.AiInsights
                .Where(i => i.BusinessId == businessId 
                         && i.Category == "profit-teaching"
                         && i.CreatedAt > DateTime.UtcNow.AddMinutes(-CACHE_FRESHNESS_MINUTES))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync(ct);
            
            if (cached != null)
            {
                var cacheAge = (DateTime.UtcNow - cached.CreatedAt).TotalMinutes;
                _logger.LogInformation(
                    "[InsightCoordinator] ✅ CACHE_HIT | Category: profit-teaching, Age: {Age:F1}min",
                    cacheAge);
                return cached.AiSummary;
            }
            
            // Cache miss - generate fresh insight
            _logger.LogInformation("[InsightCoordinator] ⚠️ CACHE_MISS | Category: profit-teaching | Generating fresh insight");
            
            var periodEnd = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var periodStart = periodEnd.AddDays(-periodDays);

            var margins = await _scanner.ScanProfitMarginsAsync(businessId, periodStart, periodEnd, ct);
            var teaching = await _formatter.FormatProfitTeachingAsync(margins, ct);

            // Save to cache for future requests
            var insight = new AiInsight
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                Category = "profit-teaching",
                DetectorVersion = "v1.0.0",
                RawFindings = JsonSerializer.Serialize(margins),
                AiSummary = teaching,
                CreatedAt = DateTime.UtcNow,
                SourceEvent = "on-demand-sales"
            };
            _aiDb.AiInsights.Add(insight);
            await _aiDb.SaveChangesAsync(ct);
            
            _logger.LogInformation("[InsightCoordinator] 💾 CACHED | Category: profit-teaching");

            return teaching;
        }

        /// <summary>
        /// Gets Expense Forecast insight (seasonality and trends).
        /// Sprint 2: Now checks cache first to avoid redundant API calls.
        /// </summary>
        public async Task<string> GetExpenseForecastAsync(
            int businessId,
            DateTime? targetMonth = null,
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "[InsightCoordinator] GetExpenseForecast | Checking cache | BusinessId: {BusinessId}, TargetMonth: {TargetMonth}",
                businessId, targetMonth);

            // Check cache first
            var cached = await _aiDb.AiInsights
                .Where(i => i.BusinessId == businessId 
                         && i.Category == "expense-forecast"
                         && i.CreatedAt > DateTime.UtcNow.AddMinutes(-CACHE_FRESHNESS_MINUTES))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync(ct);
            
            if (cached != null)
            {
                var cacheAge = (DateTime.UtcNow - cached.CreatedAt).TotalMinutes;
                _logger.LogInformation(
                    "[InsightCoordinator] ✅ CACHE_HIT | Category: expense-forecast, Age: {Age:F1}min",
                    cacheAge);
                return cached.AiSummary;
            }
            
            // Cache miss - generate fresh insight
            _logger.LogInformation("[InsightCoordinator] ⚠️ CACHE_MISS | Category: expense-forecast | Generating fresh insight");
            
            var effectiveMonth = targetMonth ?? DateTime.UtcNow;

            var trends = await _scanner.ScanExpenseTrendsAsync(businessId, effectiveMonth, ct);
            var forecast = await _formatter.FormatExpenseForecastAsync(trends, ct);

            // Save to cache for future requests
            var insight = new AiInsight
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                Category = "expense-forecast",
                DetectorVersion = "v1.0.0",
                RawFindings = JsonSerializer.Serialize(trends),
                AiSummary = forecast,
                CreatedAt = DateTime.UtcNow,
                SourceEvent = "on-demand-expenses"
            };
            _aiDb.AiInsights.Add(insight);
            await _aiDb.SaveChangesAsync(ct);
            
            _logger.LogInformation("[InsightCoordinator] 💾 CACHED | Category: expense-forecast");

            return forecast;
        }    }

    /// <summary>
    /// Result of insight coordination operation.
    /// </summary>
    public sealed class InsightCoordinatorResult
    {
        public int BusinessId { get; set; }
        public List<AiInsight> Insights { get; set; } = new();
        public string DetectorVersion { get; set; } = string.Empty;
        public bool CacheHit { get; set; }
        public bool WasCached { get; set; }
        public int CacheAgeMinutes { get; set; }
        public long ScanDurationMs { get; set; }
        public long TotalDurationMs { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
