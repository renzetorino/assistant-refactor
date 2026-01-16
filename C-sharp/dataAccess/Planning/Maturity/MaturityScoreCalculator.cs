// ============================================================================
// COMMENTED OUT: Business Maturity Score Calculator
// ============================================================================
// REASON: Did not meet team and advisor standards - calculation logic incomplete
// CRITICAL ISSUES:
//   1. Agility Score: Uses hardcoded 12-hour baseline instead of real ActivityLog data
//      - Line 138: var estimatedAvgResponseHours = 12m; // ❌ PLACEHOLDER!
//   2. Trust Score: Based on inventory update COUNT, not actual forecast adherence
//      - Line 224-265: TODO comment acknowledges incomplete implementation
//      - Requires purchase_orders and forecast tables that don't exist yet
//   3. Small Datasets: Returns default score of 50 when no data available
//      - Makes results unreliable for decision-making
// ARCHITECTURE:
//   - Three dimensions: Agility (35%), Discipline (40%), Trust (25%)
//   - Health Score = weighted average
//   - Maturity Levels: Strategist (85+), Manager (70-84), Operator (55-69), Novice (<55)
// PLAN: Refactor with real metrics collection when infrastructure is ready
// DATE COMMENTED: January 15, 2026
// ============================================================================

/*
using System;
using System.Linq;
using System.Threading;
using System.Threading.Task;
using dataAccess.Contracts;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dataAccess.Planning.Maturity
{
    /// <summary>
    /// Calculates business maturity scores based on owner behavioral metrics.
    /// </summary>
    public sealed class MaturityScoreCalculator
    {
        private readonly AppDbContext _context;
        private readonly ILogger<MaturityScoreCalculator> _logger;

        // Scoring thresholds (can be moved to configuration)
        private const decimal EXCELLENT_RESPONSE_HOURS = 6;
        private const decimal GOOD_RESPONSE_HOURS = 24;
        private const decimal EXCELLENT_LAG_DAYS = 1;
        private const decimal GOOD_LAG_DAYS = 3;
        private const decimal EXCELLENT_ADHERENCE = 95;
        private const decimal GOOD_ADHERENCE = 80;

        public MaturityScoreCalculator(AppDbContext context, ILogger<MaturityScoreCalculator> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Calculates maturity scores for a business over a specified time period.
        /// </summary>
        public async Task<MaturityScoreResult> CalculateScoresAsync(
            int businessId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            CancellationToken ct = default)
        {
            try
            {
                var end = endDate ?? DateTime.UtcNow;
                var start = startDate ?? end.AddMonths(-3);

                _logger.LogInformation(
                    "[MaturityScoreCalculator] Calculating scores | BusinessId: {BusinessId}, Period: {Start} to {End}",
                    businessId, start, end);

                var metrics = new MaturityMetrics();
                
                // Calculate Agility Score (response time to stockout alerts)
                var agilityScore = await CalculateAgilityScoreAsync(businessId, start, end, metrics, ct);

                // Calculate Discipline Score (bookkeeping lag)
                var disciplineScore = await CalculateDisciplineScoreAsync(businessId, start, end, metrics, ct);

                // Calculate Trust Score (forecast adherence)
                var trustScore = await CalculateTrustScoreAsync(businessId, start, end, metrics, ct);

                // Compute overall health score (weighted average)
                var healthScore = CalculateHealthScore(agilityScore, disciplineScore, trustScore);

                var result = new MaturityScoreResult
                {
                    BusinessId = businessId,
                    AgilityScore = agilityScore,
                    DisciplineScore = disciplineScore,
                    TrustScore = trustScore,
                    HealthScore = healthScore,
                    StartDate = start,
                    EndDate = end,
                    Metrics = metrics,
                    Success = true
                };

                _logger.LogInformation(
                    "[MaturityScoreCalculator] Scores calculated | BusinessId: {BusinessId}, Health: {Health}, Agility: {Agility}, Discipline: {Discipline}, Trust: {Trust}",
                    businessId, healthScore, agilityScore, disciplineScore, trustScore);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MaturityScoreCalculator] Failed to calculate scores | BusinessId: {BusinessId}", businessId);
                return new MaturityScoreResult
                {
                    BusinessId = businessId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Agility Score: Measures response time to stockout alerts.
        /// Lower response time = higher score.
        /// </summary>
        private async Task<decimal> CalculateAgilityScoreAsync(
            int businessId,
            DateTime start,
            DateTime end,
            MaturityMetrics metrics,
            CancellationToken ct)
        {
            try
            {
                // Find products that had low stock situations and were subsequently restocked
                var restockEvents = await _context.ProductCategories
                    .Where(pc => _context.Products.Any(p => 
                        p.ProductId == pc.ProductId && 
                        p.BusinessId == businessId))
                    .Where(pc => pc.UpdatedStock >= start && pc.UpdatedStock <= end)
                    .Where(pc => pc.CurrentStock > pc.ReorderPoint) // Successfully restocked
                    .Select(pc => new
                    {
                        pc.UpdatedStock,
                        pc.CurrentStock,
                        pc.ReorderPoint
                    })
                    .ToListAsync(ct);

                metrics.StockoutAlertCount = restockEvents.Count;

                if (restockEvents.Count == 0)
                {
                    _logger.LogDebug("[MaturityScoreCalculator] No restock events found, defaulting agility to 50");
                    return 50; // Neutral score when no data
                }

                // Estimate average response time (simplified - in real system would use activitylog)
                // For now, assume 12-hour average response as baseline
                var estimatedAvgResponseHours = 12m;
                metrics.AvgResponseTimeHours = estimatedAvgResponseHours;

                // Score: 100 if <= 6 hours, 80 if <= 24 hours, decreasing linearly after that
                var score = estimatedAvgResponseHours switch
                {
                    <= EXCELLENT_RESPONSE_HOURS => 100m,
                    <= GOOD_RESPONSE_HOURS => 80m - ((estimatedAvgResponseHours - EXCELLENT_RESPONSE_HOURS) * 2),
                    _ => Math.Max(50m, 80m - ((estimatedAvgResponseHours - GOOD_RESPONSE_HOURS) * 1.5m))
                };

                return Math.Clamp(score, 0, 100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MaturityScoreCalculator] Agility calculation failed, returning default");
                return 50;
            }
        }

        /// <summary>
        /// Discipline Score: Measures bookkeeping lag (time between occurrence and entry).
        /// Lower lag = higher score.
        /// </summary>
        private async Task<decimal> CalculateDisciplineScoreAsync(
            int businessId,
            DateTime start,
            DateTime end,
            MaturityMetrics metrics,
            CancellationToken ct)
        {
            try
            {
                var expenses = await _context.Expenses
                    .Where(e => e.BusinessId == businessId)
                    .Where(e => e.CreatedAt >= start && e.CreatedAt <= end)
                    .Select(e => new
                    {
                        e.OccurredOn,
                        e.CreatedAt
                    })
                    .ToListAsync(ct);

                metrics.ExpenseCount = expenses.Count;

                if (expenses.Count == 0)
                {
                    _logger.LogDebug("[MaturityScoreCalculator] No expenses found, defaulting discipline to 50");
                    return 50;
                }

                // Calculate average lag in days
                var lags = expenses.Select(e =>
                {
                    var occurredDateTime = e.OccurredOn.ToDateTime(TimeOnly.MinValue);
                    return (e.CreatedAt - occurredDateTime).TotalDays;
                }).ToList();

                var avgLag = (decimal)lags.Average();
                metrics.AvgBookkeepingLagDays = Math.Max(0, avgLag);

                // Score: 100 if <= 1 day, 85 if <= 3 days, decreasing after that
                var score = avgLag switch
                {
                    <= EXCELLENT_LAG_DAYS => 100m,
                    <= GOOD_LAG_DAYS => 85m - ((avgLag - EXCELLENT_LAG_DAYS) * 5),
                    _ => Math.Max(40m, 85m - ((avgLag - GOOD_LAG_DAYS) * 3))
                };

                return Math.Clamp(score, 0, 100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MaturityScoreCalculator] Discipline calculation failed, returning default");
                return 50;
            }
        }

        /// <summary>
        /// Trust Score: Measures adherence to AI forecasts.
        /// Higher adherence = higher score.
        /// Note: Simplified calculation - assumes moderate adherence until forecast tracking is implemented.
        /// </summary>
        private async Task<decimal> CalculateTrustScoreAsync(
            int businessId,
            DateTime start,
            DateTime end,
            MaturityMetrics metrics,
            CancellationToken ct)
        {
            try
            {
                // TODO: Implement forecast adherence tracking when purchase_orders and forecast tables are available
                // For now, calculate based on inventory management consistency
                
                var inventoryConsistency = await _context.ProductCategories
                    .Where(pc => _context.Products.Any(p => p.ProductId == pc.ProductId && p.BusinessId == businessId))
                    .Where(pc => pc.UpdatedStock >= start && pc.UpdatedStock <= end)
                    .CountAsync(ct);

                metrics.ForecastComparisonCount = inventoryConsistency;

                if (inventoryConsistency == 0)
                {
                    _logger.LogDebug("[MaturityScoreCalculator] No inventory updates found, defaulting trust to 50");
                    return 50;
                }

                // Simplified calculation: assume moderate adherence based on inventory updates
                var adherencePercent = Math.Min(100m, 60m + (inventoryConsistency * 2m));
                metrics.ForecastAdherencePercent = adherencePercent;

                // Score: 100 if >= 95%, 80 if >= 80%, decreasing after that
                var score = adherencePercent switch
                {
                    >= EXCELLENT_ADHERENCE => 100m,
                    >= GOOD_ADHERENCE => 80m + ((adherencePercent - GOOD_ADHERENCE) * 1.33m),
                    _ => adherencePercent * 0.8m
                };

                return Math.Clamp(score, 0, 100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MaturityScoreCalculator] Trust calculation failed, returning default");
                return 50;
            }
        }

        /// <summary>
        /// Calculates overall health score as weighted average.
        /// Agility: 35%, Discipline: 40%, Trust: 25%
        /// </summary>
        private decimal CalculateHealthScore(decimal agility, decimal discipline, decimal trust)
        {
            var healthScore = (agility * 0.35m) + (discipline * 0.40m) + (trust * 0.25m);
            return Math.Round(healthScore, 1);
        }

        /// <summary>
        /// Determines maturity level based on health score.
        /// </summary>
        public static string GetMaturityLevel(decimal healthScore)
        {
            return healthScore switch
            {
                >= 85 => "Strategist",
                >= 70 => "Manager",
                >= 55 => "Operator",
                _ => "Novice"
            };
        }
    }
}
*/
