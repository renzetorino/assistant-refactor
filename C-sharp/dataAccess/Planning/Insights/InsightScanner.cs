using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Contracts;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dataAccess.Planning.Insights
{
    /// <summary>
    /// Scans business data for actionable insights using deterministic SQL detectors.
    /// </summary>
    public sealed class InsightScanner
    {
        private readonly AppDbContext _appDb;
        private readonly ILogger<InsightScanner> _logger;

        // Hardcoded thresholds for Sprint 1-2 (migrate to env vars before production)
        private const decimal MARGIN_THRESHOLD = 0.20m;      // 20% minimum profit margin
        private const int DEAD_STOCK_DAYS = 30;              // No sales/updates in 30 days
        private const decimal BUDGET_WARN_PCT = 0.80m;       // 80% budget burn triggers warning

        private const string DETECTOR_VERSION = "v1.0.0";    // Increment when detector logic changes

        public InsightScanner(AppDbContext appDb, ILogger<InsightScanner> logger)
        {
            _appDb = appDb;
            _logger = logger;
        }

        /// <summary>
        /// Runs all 5 detectors for the specified business and returns consolidated findings.
        /// </summary>
        public async Task<InsightScanResult> ScanAllAsync(int businessId, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var result = new InsightScanResult
            {
                BusinessId = businessId,
                DetectorVersion = DETECTOR_VERSION,
                ScanStartedAt = DateTime.UtcNow
            };

            _logger.LogInformation(
                "[InsightScanner] Starting scan for BusinessId: {BusinessId}, Version: {Version}",
                businessId, DETECTOR_VERSION);

            var detectorTasks = new[]
            {
                RunDetectorAsync("StockoutRisk", () => DetectStockoutRisk(businessId, ct)),
                RunDetectorAsync("DeadStock", () => DetectDeadStock(businessId, ct)),
                RunDetectorAsync("MarginHealth", () => DetectMarginHealth(businessId, ct)),
                RunDetectorAsync("BudgetBurnRate", () => DetectBudgetBurnRate(businessId, ct)),
                RunDetectorAsync("OverBudgetCategories", () => DetectOverBudgetCategories(businessId, ct))
            };

            result.DetectorsExecuted = detectorTasks.Length;

            try
            {
                var allFindings = await Task.WhenAll(detectorTasks);
                result.Findings = allFindings
                    .Where(f => f.findings != null)
                    .SelectMany(f => f.findings!)
                    .ToList();

                result.DetectorsFailed = allFindings.Count(f => !f.success);
                result.Success = result.DetectorsFailed == 0;

                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                result.ScanCompletedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "[InsightScanner] Scan completed for BusinessId: {BusinessId} | " +
                    "Findings: {FindingCount}, Failed: {FailedCount}, Duration: {Duration}ms",
                    businessId, result.Findings.Count, result.DetectorsFailed, result.DurationMs);
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.DurationMs = sw.ElapsedMilliseconds;
                result.ScanCompletedAt = DateTime.UtcNow;

                _logger.LogError(ex,
                    "[InsightScanner] Scan failed for BusinessId: {BusinessId}", businessId);
            }

            return result;
        }

        /// <summary>
        /// Wraps detector execution with error handling.
        /// </summary>
        private async Task<(bool success, List<DetectorFinding>? findings)> RunDetectorAsync(
            string detectorName,
            Func<Task<List<DetectorFinding>>> detector)
        {
            try
            {
                var findings = await detector();
                return (true, findings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InsightScanner] Detector {DetectorName} failed", detectorName);
                return (false, null);
            }
        }

        // ========================================
        // DETECTOR 1: Stockout Risk
        // ========================================
        /// <summary>
        /// Identifies products where current_stock <= reorder_point.
        /// </summary>
        private async Task<List<DetectorFinding>> DetectStockoutRisk(int businessId, CancellationToken ct)
        {
            var findings = new List<DetectorFinding>();

            var query = from pc in _appDb.ProductCategories
                        join p in _appDb.Products on pc.ProductId equals p.ProductId
                        where p.BusinessId == businessId
                           && pc.CurrentStock <= pc.ReorderPoint
                        select new
                        {
                            ProductId = p.ProductId,
                            ProductName = p.ProductName,
                            Color = pc.Color,
                            AgeSize = pc.AgeSize,
                            CurrentStock = pc.CurrentStock,
                            ReorderPoint = pc.ReorderPoint
                        };

            var results = await query.ToListAsync(ct);

            foreach (var item in results)
            {
                var variantDisplay = string.IsNullOrWhiteSpace(item.Color) && string.IsNullOrWhiteSpace(item.AgeSize)
                    ? item.ProductName
                    : $"{item.ProductName} ({item.Color ?? ""} {item.AgeSize ?? ""})".Trim();

                findings.Add(new DetectorFinding
                {
                    Category = "inventory",
                    Severity = item.CurrentStock == 0 ? "critical" : "warning",
                    Title = item.CurrentStock == 0 ? "Out of Stock" : "Low Stock Alert",
                    Description = $"{variantDisplay} has only {item.CurrentStock} units left (reorder at {item.ReorderPoint}).",
                    EntityId = item.ProductId.ToString(),
                    EntityName = variantDisplay,
                    MetricValue = item.CurrentStock,
                    ThresholdValue = item.ReorderPoint,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        productId = item.ProductId,
                        color = item.Color,
                        ageSize = item.AgeSize,
                        currentStock = item.CurrentStock,
                        reorderPoint = item.ReorderPoint
                    })
                });
            }

            _logger.LogDebug(
                "[DetectStockoutRisk] BusinessId: {BusinessId}, Found: {Count}",
                businessId, findings.Count);

            return findings;
        }

        // ========================================
        // DETECTOR 2: Dead Stock
        // ========================================
        /// <summary>
        /// Identifies products with no stock updates in last 30 days and non-zero inventory.
        /// </summary>
        private async Task<List<DetectorFinding>> DetectDeadStock(int businessId, CancellationToken ct)
        {
            var findings = new List<DetectorFinding>();
            var cutoffDate = DateTime.UtcNow.AddDays(-DEAD_STOCK_DAYS);

            var query = from pc in _appDb.ProductCategories
                        join p in _appDb.Products on pc.ProductId equals p.ProductId
                        where p.BusinessId == businessId
                           && pc.CurrentStock > 0
                           && (pc.UpdatedStock == null || pc.UpdatedStock < cutoffDate)
                        select new
                        {
                            ProductId = p.ProductId,
                            ProductName = p.ProductName,
                            Color = pc.Color,
                            AgeSize = pc.AgeSize,
                            CurrentStock = pc.CurrentStock,
                            UpdatedStock = pc.UpdatedStock,
                            Cost = pc.Cost
                        };

            var results = await query.ToListAsync(ct);

            foreach (var item in results)
            {
                var variantDisplay = string.IsNullOrWhiteSpace(item.Color) && string.IsNullOrWhiteSpace(item.AgeSize)
                    ? item.ProductName
                    : $"{item.ProductName} ({item.Color ?? ""} {item.AgeSize ?? ""})".Trim();

                var daysSinceUpdate = item.UpdatedStock.HasValue
                    ? (int)(DateTime.UtcNow - item.UpdatedStock.Value).TotalDays
                    : 999;

                var capitalTied = item.CurrentStock * item.Cost;

                findings.Add(new DetectorFinding
                {
                    Category = "inventory",
                    Severity = daysSinceUpdate > 60 ? "warning" : "info",
                    Title = "Dead Stock Alert",
                    Description = $"{variantDisplay} hasn't moved in {daysSinceUpdate} days. {item.CurrentStock} units tying up ₱{capitalTied:N2}.",
                    EntityId = item.ProductId.ToString(),
                    EntityName = variantDisplay,
                    MetricValue = daysSinceUpdate,
                    ThresholdValue = DEAD_STOCK_DAYS,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        productId = item.ProductId,
                        color = item.Color,
                        ageSize = item.AgeSize,
                        currentStock = item.CurrentStock,
                        daysSinceUpdate = daysSinceUpdate,
                        capitalTied = capitalTied
                    })
                });
            }

            _logger.LogDebug(
                "[DetectDeadStock] BusinessId: {BusinessId}, Found: {Count}",
                businessId, findings.Count);

            return findings;
        }

        // ========================================
        // DETECTOR 3: Margin Health
        // ========================================
        /// <summary>
        /// Identifies products with profit margin below 20%.
        /// </summary>
        private async Task<List<DetectorFinding>> DetectMarginHealth(int businessId, CancellationToken ct)
        {
            var findings = new List<DetectorFinding>();

            var query = from pc in _appDb.ProductCategories
                        join p in _appDb.Products on pc.ProductId equals p.ProductId
                        where p.BusinessId == businessId
                           && pc.Price > 0
                        select new
                        {
                            ProductId = p.ProductId,
                            ProductName = p.ProductName,
                            Color = pc.Color,
                            AgeSize = pc.AgeSize,
                            Price = pc.Price,
                            Cost = pc.Cost
                        };

            var results = await query.ToListAsync(ct);

            foreach (var item in results)
            {
                var margin = item.Price > 0 ? (item.Price - item.Cost) / item.Price : 0;

                if (margin < MARGIN_THRESHOLD)
                {
                    var variantDisplay = string.IsNullOrWhiteSpace(item.Color) && string.IsNullOrWhiteSpace(item.AgeSize)
                        ? item.ProductName
                        : $"{item.ProductName} ({item.Color ?? ""} {item.AgeSize ?? ""})".Trim();

                    var marginPct = margin * 100;

                    findings.Add(new DetectorFinding
                    {
                        Category = "sales",
                        Severity = margin < 0 ? "critical" : "warning",
                        Title = margin < 0 ? "Losing Money" : "Low Profit Margin",
                        Description = $"{variantDisplay} has only {marginPct:F1}% margin (₱{item.Cost} cost → ₱{item.Price} price).",
                        EntityId = item.ProductId.ToString(),
                        EntityName = variantDisplay,
                        MetricValue = marginPct,
                        ThresholdValue = MARGIN_THRESHOLD * 100,
                        Metadata = JsonSerializer.Serialize(new
                        {
                            productId = item.ProductId,
                            color = item.Color,
                            ageSize = item.AgeSize,
                            price = item.Price,
                            cost = item.Cost,
                            marginPercent = marginPct
                        })
                    });
                }
            }

            _logger.LogDebug(
                "[DetectMarginHealth] BusinessId: {BusinessId}, Found: {Count}",
                businessId, findings.Count);

            return findings;
        }

        // ========================================
        // DETECTOR 4: Budget Burn Rate
        // ========================================
        /// <summary>
        /// Compares current month expenses vs budget allocation.
        /// </summary>
        private async Task<List<DetectorFinding>> DetectBudgetBurnRate(int businessId, CancellationToken ct)
        {
            var findings = new List<DetectorFinding>();
            var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);

            // Get budget for current month
            var budget = await _appDb.Budgets
                .Where(b => b.BusinessId == businessId && b.MonthYear == currentMonth)
                .FirstOrDefaultAsync(ct);

            if (budget == null || !budget.MonthlyBudgetAmount.HasValue || budget.MonthlyBudgetAmount.Value == 0)
            {
                // No budget set - skip this detector
                return findings;
            }

            var budgetAmount = budget.MonthlyBudgetAmount.Value;

            // Sum expenses for current month
            var firstDayOfMonth = new DateTime(currentMonth.Year, currentMonth.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var totalExpenses = await _appDb.Expenses
                .Where(e => e.BusinessId == businessId
                         && e.OccurredOn >= DateOnly.FromDateTime(firstDayOfMonth)
                         && e.OccurredOn <= DateOnly.FromDateTime(lastDayOfMonth))
                .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

            var burnRate = budgetAmount > 0 ? totalExpenses / budgetAmount : 0;

            if (burnRate >= BUDGET_WARN_PCT)
            {
                var burnPct = burnRate * 100;

                findings.Add(new DetectorFinding
                {
                    Category = "finance",
                    Severity = burnRate >= 1.0m ? "critical" : "warning",
                    Title = burnRate >= 1.0m ? "Budget Exceeded" : "Budget Alert",
                    Description = $"You've spent ₱{totalExpenses:N2} ({burnPct:F0}%) of your ₱{budgetAmount:N2} monthly budget.",
                    EntityId = budget.BudgetId.ToString(),
                    EntityName = $"{currentMonth:MMMM yyyy} Budget",
                    MetricValue = burnPct,
                    ThresholdValue = BUDGET_WARN_PCT * 100,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        budgetId = budget.BudgetId,
                        monthYear = currentMonth.ToString("yyyy-MM"),
                        budgetAmount = budgetAmount,
                        totalExpenses = totalExpenses,
                        burnRatePercent = burnPct
                    })
                });
            }

            _logger.LogDebug(
                "[DetectBudgetBurnRate] BusinessId: {BusinessId}, BurnRate: {BurnRate:P}, Found: {Count}",
                businessId, burnRate, findings.Count);

            return findings;
        }

        // ========================================
        // DETECTOR 5: Over-Budget Categories
        // ========================================
        /// <summary>
        /// NOTE: Current schema doesn't support category-level budget allocations.
        /// BudgetHistory only tracks old_amount/new_amount changes, not per-category limits.
        /// This detector will return empty until the schema is extended with a budget_categories table.
        /// </summary>
        private async Task<List<DetectorFinding>> DetectOverBudgetCategories(int businessId, CancellationToken ct)
        {
            var findings = new List<DetectorFinding>();

            // TODO: When schema supports category allocations, implement:
            // 1. Query budget_categories table for allocated amounts per category
            // 2. SUM expenses by category_id for current month
            // 3. Compare actual vs allocated, flag overages

            _logger.LogDebug(
                "[DetectOverBudgetCategories] BusinessId: {BusinessId}, Skipped (schema not ready)",
                businessId);

            return await Task.FromResult(findings);
        }
    }
}
