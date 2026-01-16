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
        /// Runs all 4 specialized scanners for the specified business and returns consolidated findings.
        /// Updated in Sprint 4 to use new specialized insight scanners.
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
                "[InsightScanner] Starting specialized scan for BusinessId: {BusinessId}, Version: {Version}",
                businessId, DETECTOR_VERSION);

            // Sprint 4: Call 4 specialized scanners SEQUENTIALLY (DbContext is NOT thread-safe)
            // Note: We run these sequentially to avoid "A second operation was started on this context instance" error
            var allScanResults = new List<(bool success, List<DetectorFinding>? findings)>();

            try
            {
                allScanResults.Add(await RunSpecializedScannerAsync("MorningBriefing", async () => await ScanTop3PrioritiesForBatchAsync(businessId, ct)));
                allScanResults.Add(await RunSpecializedScannerAsync("InventoryRisk", async () => await ScanAllProductsRiskForBatchAsync(businessId, ct)));
                allScanResults.Add(await RunSpecializedScannerAsync("ProfitTeaching", async () => await ScanProfitMarginsForBatchAsync(businessId, ct)));
                allScanResults.Add(await RunSpecializedScannerAsync("ExpenseForecast", async () => await ScanExpenseTrendsForBatchAsync(businessId, ct)));

                result.DetectorsExecuted = allScanResults.Count;
                result.Findings = allScanResults
                    .Where(f => f.findings != null)
                    .SelectMany(f => f.findings!)
                    .ToList();

                result.DetectorsFailed = allScanResults.Count(f => !f.success);
                result.Success = result.DetectorsFailed == 0;

                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                result.ScanCompletedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "[InsightScanner] Specialized scan completed for BusinessId: {BusinessId} | " +
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
                    "[InsightScanner] Specialized scan failed for BusinessId: {BusinessId}", businessId);
            }

            return result;
        }

        /// <summary>
        /// Wraps specialized scanner execution with error handling (Sprint 4).
        /// </summary>
        private async Task<(bool success, List<DetectorFinding>? findings)> RunSpecializedScannerAsync(
            string scannerName,
            Func<Task<List<DetectorFinding>>> scannerFunc)
        {
            try
            {
                var findings = await scannerFunc();
                _logger.LogDebug(
                    "[InsightScanner] ✅ {ScannerName} completed | Findings: {Count}",
                    scannerName, findings.Count);
                return (true, findings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[InsightScanner] ❌ {ScannerName} failed",
                    scannerName);
                return (false, null);
            }
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

        // ========================================
        // SPECIALIZED INSIGHTS (Sprint 2)
        // ========================================

        /// <summary>
        /// Scans for top 3 priority issues across all modules for morning briefing.
        /// </summary>
        public async Task<List<CriticalIssue>> ScanTop3PrioritiesAsync(int businessId, CancellationToken ct = default)
        {
            var issues = new List<CriticalIssue>();

            // 1. Critical stockouts (priority 1)
            // Sprint 4: Added OrderBy for deterministic query results
            var stockouts = await (from pc in _appDb.ProductCategories
                                  join p in _appDb.Products on pc.ProductId equals p.ProductId
                                  where p.BusinessId == businessId
                                     && pc.CurrentStock == 0
                                  select new CriticalIssue
                                  {
                                      Category = "inventory",
                                      Severity = "critical",
                                      Title = "Stockout Alert",
                                      Description = $"{p.ProductName} is completely out of stock",
                                      EntityName = p.ProductName,
                                      MetricValue = pc.CurrentStock,
                                      Priority = 1
                                  }
                                  ).OrderBy(x => x.EntityName).Take(3).ToListAsync(ct);
            issues.AddRange(stockouts);

            // 2. Low stock warnings (priority 2)
            // Sprint 4: Added OrderBy for deterministic query results
            if (issues.Count < 3)
            {
                var lowStock = await (from pc in _appDb.ProductCategories
                                     join p in _appDb.Products on pc.ProductId equals p.ProductId
                                     where p.BusinessId == businessId
                                        && pc.CurrentStock > 0
                                        && pc.CurrentStock <= pc.ReorderPoint
                                     select new CriticalIssue
                                     {
                                         Category = "inventory",
                                         Severity = "warning",
                                         Title = "Low Stock",
                                         Description = $"{p.ProductName} below reorder point ({pc.CurrentStock} units left)",
                                         EntityName = p.ProductName,
                                         MetricValue = pc.CurrentStock,
                                         Priority = 2
                                     }
                                     ).OrderBy(x => x.MetricValue).Take(3 - issues.Count).ToListAsync(ct);
                issues.AddRange(lowStock);
            }

            // 3. Budget warnings (priority 3)
            if (issues.Count < 3)
            {
                var now = DateTime.UtcNow;
                var monthStart = new DateOnly(now.Year, now.Month, 1);

                // Sprint 4: Added OrderBy for deterministic query results
                var budgetWarnings = await (from b in _appDb.Budgets
                                           where b.BusinessId == businessId
                                              && b.MonthlyBudgetAmount.HasValue
                                              && b.MonthlyBudgetAmount.Value > 0
                                           select new
                                           {
                                               Budget = b,
                                               Spent = _appDb.Expenses
                                                   .Where(e => e.BusinessId == businessId
                                                            && e.OccurredOn >= monthStart)
                                                   .Sum(e => (decimal?)e.Amount) ?? 0m
                                           })
                                           .Where(x => x.Spent / x.Budget.MonthlyBudgetAmount.Value >= BUDGET_WARN_PCT)
                                           .Select(x => new CriticalIssue
                                           {
                                               Category = "finance",
                                               Severity = "warning",
                                               Title = "Budget Alert",
                                               Description = $"Spending at {(x.Spent / x.Budget.MonthlyBudgetAmount.Value * 100):F0}% of monthly budget",
                                               EntityName = "Monthly Budget",
                                               MetricValue = x.Spent / x.Budget.MonthlyBudgetAmount.Value * 100,
                                               Priority = 3
                                           })
                                           .OrderByDescending(x => x.MetricValue)
                                           .Take(3 - issues.Count).ToListAsync(ct);
                issues.AddRange(budgetWarnings);
            }

            return issues.OrderBy(i => i.Priority).Take(3).ToList();
        }

        /// <summary>
        /// Analyzes stockout risk for a specific product.
        /// Note: Limited by current schema - Product table doesn't have price/cost/lastSoldAt fields.
        /// </summary>
        public async Task<ProductRiskData?> ScanProductRiskAsync(int businessId, int productId, CancellationToken ct = default)
        {
            var product = await _appDb.Products
                .Where(p => p.BusinessId == businessId && p.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (product == null) return null;

            var productCategory = await _appDb.ProductCategories
                .Where(pc => pc.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (productCategory == null) return null;

            // Calculate sales velocity from orders/orderitems (last 30 days)
            var thirtyDaysAgo = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-30), DateTimeKind.Unspecified);
            var salesLast30Days = await (from oi in _appDb.OrderItems
                                        join o in _appDb.Orders on oi.OrderId equals o.OrderId
                                        where oi.ProductId == productId
                                           && o.BusinessId == businessId
                                           && o.OrderDate >= thirtyDaysAgo
                                        select oi.Quantity).SumAsync(ct);

            var avgDailySales = salesLast30Days / 30m;
            int? daysUntilStockout = avgDailySales > 0
                ? (int?)(productCategory.CurrentStock / avgDailySales)
                : null;

            // Get recent price from orderitems
            var recentPrice = await (from oi in _appDb.OrderItems
                                    join o in _appDb.Orders on oi.OrderId equals o.OrderId
                                    where oi.ProductId == productId
                                       && o.BusinessId == businessId
                                    orderby o.OrderDate descending
                                    select oi.UnitPrice).FirstOrDefaultAsync(ct);

            var potentialLostRevenue = daysUntilStockout.HasValue && daysUntilStockout.Value < 7 && recentPrice > 0
                ? (decimal?)(avgDailySales * 7 * recentPrice)  // 1 week of lost sales
                : (decimal?)null;

            // Calculate days since last sale
            var lastOrderDate = await (from oi in _appDb.OrderItems
                                      join o in _appDb.Orders on oi.OrderId equals o.OrderId
                                      where oi.ProductId == productId
                                         && o.BusinessId == businessId
                                      orderby o.OrderDate descending
                                      select (DateTime?)o.OrderDate).FirstOrDefaultAsync(ct);

            var daysSinceLastSale = lastOrderDate.HasValue
                ? (int?)(DateTime.UtcNow - lastOrderDate.Value).TotalDays
                : null;

            var riskType = productCategory.CurrentStock <= productCategory.ReorderPoint
                ? "stockout"
                : (daysSinceLastSale > DEAD_STOCK_DAYS ? "dead_stock" : "healthy");

            return new ProductRiskData
            {
                ProductId = productId,
                ProductName = product.ProductName,
                CurrentStock = productCategory.CurrentStock,
                ReorderPoint = productCategory.ReorderPoint,
                AverageDailySales = avgDailySales,
                DaysUntilStockout = daysUntilStockout,
                Price = recentPrice,
                PotentialLostRevenue = potentialLostRevenue,
                DaysSinceLastSale = daysSinceLastSale,
                RiskType = riskType
            };
        }

        /// <summary>
        /// Analyzes profit margins by category for the specified period.
        /// Note: Limited profit calculation due to missing cost data in schema.
        /// </summary>
        public async Task<List<MarginData>> ScanProfitMarginsAsync(
            int businessId,
            DateTime periodStart,
            DateTime periodEnd,
            CancellationToken ct = default)
        {
            // Ensure DateTimes are Unspecified for PostgreSQL compatibility
            periodStart = DateTime.SpecifyKind(periodStart, DateTimeKind.Unspecified);
            periodEnd = DateTime.SpecifyKind(periodEnd, DateTimeKind.Unspecified);

            var margins = await (from oi in _appDb.OrderItems
                                join o in _appDb.Orders on oi.OrderId equals o.OrderId
                                join p in _appDb.Products on oi.ProductId equals p.ProductId
                                where o.BusinessId == businessId
                                   && o.OrderDate >= periodStart
                                   && o.OrderDate <= periodEnd
                                group new { oi, o, p } by p.ProductName into g
                                select new MarginData
                                {
                                    Category = g.Key,
                                    Revenue = g.Sum(x => x.oi.Subtotal),
                                    Cost = 0, // Cost data not available in current schema
                                    Profit = g.Sum(x => x.oi.Subtotal), // Approximation - need cost data
                                    ProfitMarginPercent = 0, // Cannot calculate without cost data
                                    TransactionCount = g.Count()
                                })
                                .OrderByDescending(m => m.Revenue)
                                .ToListAsync(ct);

            return margins;
        }

        /// <summary>
        /// Analyzes expense trends with seasonality detection for the target month.
        /// </summary>
        public async Task<List<SpendingTrend>> ScanExpenseTrendsAsync(
            int businessId,
            DateTime targetMonth,
            CancellationToken ct = default)
        {
            var monthStart = new DateOnly(targetMonth.Year, targetMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            // Get last 6 months for comparison
            var sixMonthsAgo = monthStart.AddMonths(-6);

            var monthlyData = await (from e in _appDb.Expenses
                                    join c in _appDb.Categories on e.CategoryId equals c.Id into cGroup
                                    from c in cGroup.DefaultIfEmpty()
                                    where e.BusinessId == businessId
                                       && e.OccurredOn >= sixMonthsAgo
                                       && e.OccurredOn <= monthEnd
                                    group e by new
                                    {
                                        Year = e.OccurredOn.Year,
                                        Month = e.OccurredOn.Month,
                                        Category = c != null ? c.Name : "Uncategorized"
                                    } into g
                                    select new
                                    {
                                        g.Key.Year,
                                        g.Key.Month,
                                        g.Key.Category,
                                        TotalSpent = g.Sum(e => e.Amount)
                                    })
                                    .ToListAsync(ct);

            // Calculate averages per category
            var categoryAverages = monthlyData
                .Where(x => new DateOnly(x.Year, x.Month, 1) < monthStart)
                .GroupBy(x => x.Category)
                .ToDictionary(
                    g => g.Key,
                    g => g.Average(x => x.TotalSpent)
                );

            // Analyze target month
            var targetMonthData = monthlyData
                .Where(x => x.Year == targetMonth.Year && x.Month == targetMonth.Month)
                .Select(x =>
                {
                    var avgSpend = categoryAverages.ContainsKey(x.Category) ? categoryAverages[x.Category] : x.TotalSpent;
                    var deviation = avgSpend > 0 ? ((x.TotalSpent - avgSpend) / avgSpend) * 100 : 0;
                    var isAnomaly = Math.Abs(deviation) > 25; // 25% threshold

                    return new SpendingTrend
                    {
                        Year = x.Year,
                        Month = x.Month,
                        Category = x.Category,
                        TotalSpent = x.TotalSpent,
                        AverageMonthlySpend = avgSpend,
                        PercentDeviation = deviation,
                        IsAnomaly = isAnomaly,
                        AnomalyType = !isAnomaly ? "normal" : (deviation > 0 ? "spike" : "drop")
                    };
                })
                .OrderByDescending(t => Math.Abs(t.PercentDeviation))
                .ToList();

            return targetMonthData;
        }

        // ========================================
        // BATCH WRAPPERS FOR SPECIALIZED SCANNERS (Sprint 4)
        // ========================================

        /// <summary>
        /// Wraps ScanTop3PrioritiesAsync for batch processing.
        /// Returns findings in DetectorFinding format.
        /// </summary>
        private async Task<List<DetectorFinding>> ScanTop3PrioritiesForBatchAsync(int businessId, CancellationToken ct)
        {
            var issues = await ScanTop3PrioritiesAsync(businessId, ct);
            return issues.Select(issue => new DetectorFinding
            {
                Category = "morning-briefing",
                Severity = issue.Severity,
                Title = issue.Title,
                Description = issue.Description,
                EntityName = issue.EntityName,
                MetricValue = issue.MetricValue,
                ThresholdValue = null,
                DetectedAt = DateTime.UtcNow
            }).ToList();
        }

        /// <summary>
        /// Holistic inventory risk analysis for ALL products (low stock + slow moving).
        /// Replaces per-product ScanProductRiskAsync() for batch scenarios.
        /// </summary>
        private async Task<List<DetectorFinding>> ScanAllProductsRiskForBatchAsync(int businessId, CancellationToken ct)
        {
            var findings = new List<DetectorFinding>();

            // 1. Get all products with low stock (CurrentStock <= ReorderPoint)
            var lowStockProducts = await (from pc in _appDb.ProductCategories
                                         join p in _appDb.Products on pc.ProductId equals p.ProductId
                                         where p.BusinessId == businessId
                                            && pc.CurrentStock <= pc.ReorderPoint
                                         select new
                                         {
                                             p.ProductId,
                                             p.ProductName,
                                             pc.CurrentStock,
                                             pc.ReorderPoint
                                         }).ToListAsync(ct);

            foreach (var product in lowStockProducts)
            {
                var severity = product.CurrentStock == 0 ? "critical" : "warning";
                findings.Add(new DetectorFinding
                {
                    Category = "inventory-risk",
                    Severity = severity,
                    Title = product.CurrentStock == 0 ? "Stockout" : "Low Stock",
                    Description = $"{product.ProductName} has {product.CurrentStock} units (reorder at {product.ReorderPoint})",
                    EntityName = product.ProductName,
                    MetricValue = product.CurrentStock,
                    ThresholdValue = product.ReorderPoint,
                    DetectedAt = DateTime.UtcNow
                });
            }

            // 2. Get slow-moving products (no sales in last 30 days)
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var productsWithStock = await _appDb.Products
                .Where(p => p.BusinessId == businessId)
                .Select(p => p.ProductId)
                .ToListAsync(ct);

            foreach (var productId in productsWithStock)
            {
                var lastSale = await (from oi in _appDb.OrderItems
                                     join o in _appDb.Orders on oi.OrderId equals o.OrderId
                                     where oi.ProductId == productId
                                        && o.BusinessId == businessId
                                     orderby o.OrderDate descending
                                     select o.OrderDate).FirstOrDefaultAsync(ct);

                if (lastSale != default && (DateTime.UtcNow - lastSale).TotalDays > DEAD_STOCK_DAYS)
                {
                    var productName = await _appDb.Products
                        .Where(p => p.ProductId == productId)
                        .Select(p => p.ProductName)
                        .FirstOrDefaultAsync(ct);

                    var daysSinceLastSale = (int)(DateTime.UtcNow - lastSale).TotalDays;
                    findings.Add(new DetectorFinding
                    {
                        Category = "inventory-risk",
                        Severity = "info",
                        Title = "Slow-Moving Stock",
                        Description = $"{productName} hasn't sold in {daysSinceLastSale} days",
                        EntityName = productName ?? "Unknown",
                        MetricValue = daysSinceLastSale,
                        ThresholdValue = DEAD_STOCK_DAYS,
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }

            _logger.LogInformation(
                "[ScanAllProductsRiskForBatchAsync] Found {LowStock} low-stock and {SlowMoving} slow-moving products | BusinessId: {BusinessId}",
                lowStockProducts.Count, findings.Count - lowStockProducts.Count, businessId);

            return findings;
        }

        /// <summary>
        /// Wraps ScanProfitMarginsAsync for batch processing.
        /// </summary>
        private async Task<List<DetectorFinding>> ScanProfitMarginsForBatchAsync(int businessId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var last30Days = now.AddDays(-30);
            
            // Convert to unspecified kind to match PostgreSQL 'timestamp without time zone'
            var periodStart = DateTime.SpecifyKind(last30Days, DateTimeKind.Unspecified);
            var periodEnd = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);
            
            var margins = await ScanProfitMarginsAsync(businessId, periodStart, periodEnd, ct);

            return margins.Select(margin => new DetectorFinding
            {
                Category = "profit-teaching",
                Severity = "info",
                Title = "Profit Margin Analysis",
                Description = $"{margin.Category}: ₱{margin.Revenue:F2} revenue, {margin.TransactionCount} transactions",
                EntityName = margin.Category,
                MetricValue = margin.Revenue,
                ThresholdValue = null,
                DetectedAt = DateTime.UtcNow
            }).ToList();
        }

        /// <summary>
        /// Wraps ScanExpenseTrendsAsync for batch processing.
        /// </summary>
        private async Task<List<DetectorFinding>> ScanExpenseTrendsForBatchAsync(int businessId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var trends = await ScanExpenseTrendsAsync(businessId, now, ct);

            return trends.Select(trend => new DetectorFinding
            {
                Category = "expense-forecast",
                Severity = trend.IsAnomaly ? "warning" : "info",
                Title = trend.IsAnomaly ? $"Expense {trend.AnomalyType}" : "Normal Spending",
                Description = $"{trend.Category}: ₱{trend.TotalSpent:F2} ({trend.PercentDeviation:+0;-0}% vs average)",
                EntityName = trend.Category,
                MetricValue = trend.TotalSpent,
                ThresholdValue = trend.AverageMonthlySpend,
                DetectedAt = DateTime.UtcNow
            }).ToList();
        }
    }
}
