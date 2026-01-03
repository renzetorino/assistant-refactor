using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dataAccess.Contracts;
using dataAccess.Entities;
using dataAccess.Planning.Insights;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace dataAccess.Api.Tests
{
    /// <summary>
    /// Tests for InsightScanner detectors using in-memory database.
    /// </summary>
    public class InsightScannerTests : IDisposable
    {
        private readonly AppDbContext _context;
        private readonly InsightScanner _scanner;
        private const int TEST_BUSINESS_ID = 1;

        public InsightScannerTests()
        {
            // Setup in-memory database
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new AppDbContext(options);

            var logger = new Mock<ILogger<InsightScanner>>().Object;
            _scanner = new InsightScanner(_context, logger);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        // ========================================
        // DETECTOR 1: Stockout Risk Tests
        // ========================================

        [Fact]
        public async Task DetectStockoutRisk_ReturnsFindings_WhenStockBelowReorderPoint()
        {
            // Arrange
            SeedStockoutRiskData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            Assert.True(result.Success);
            var stockoutFindings = result.Findings.Where(f => f.Title.Contains("Stock")).ToList();
            Assert.Equal(2, stockoutFindings.Count);

            var criticalFinding = stockoutFindings.First(f => f.Severity == "critical");
            Assert.Equal("Out of Stock", criticalFinding.Title);
            Assert.Equal("inventory", criticalFinding.Category);
            Assert.Equal(0, criticalFinding.MetricValue);

            var warningFinding = stockoutFindings.First(f => f.Severity == "warning");
            Assert.Equal("Low Stock Alert", warningFinding.Title);
            Assert.Equal(3, warningFinding.MetricValue);
            Assert.Equal(5, warningFinding.ThresholdValue);
        }

        [Fact]
        public async Task DetectStockoutRisk_ReturnsEmpty_WhenStockAboveReorderPoint()
        {
            // Arrange
            SeedHealthyStockData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var stockoutFindings = result.Findings.Where(f => f.Title.Contains("Stock")).ToList();
            Assert.Empty(stockoutFindings);
        }

        [Fact]
        public async Task DetectStockoutRisk_FiltersBy_BusinessId()
        {
            // Arrange
            SeedStockoutRiskData();
            SeedOtherBusinessData(businessId: 2);

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var stockoutFindings = result.Findings.Where(f => f.Title.Contains("Stock")).ToList();
            Assert.Equal(2, stockoutFindings.Count); // Only from business 1
        }

        // ========================================
        // DETECTOR 2: Dead Stock Tests
        // ========================================

        [Fact]
        public async Task DetectDeadStock_ReturnsFindings_WhenNoActivityFor30Days()
        {
            // Arrange
            SeedDeadStockData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var deadStockFindings = result.Findings.Where(f => f.Title.Contains("Dead Stock")).ToList();
            Assert.Equal(2, deadStockFindings.Count);

            var finding = deadStockFindings.First();
            Assert.Equal("inventory", finding.Category);
            Assert.True(finding.MetricValue >= 30); // Days since update
        }

        [Fact]
        public async Task DetectDeadStock_IgnoresZeroStock()
        {
            // Arrange
            var product = new Product
            {
                ProductId = 100,
                ProductName = "Zero Stock Product",
                BusinessId = TEST_BUSINESS_ID,
                CreatedAt = DateTime.UtcNow
            };
            _context.Products.Add(product);

            var category = new ProductCategory
            {
                ProductCategoryId = 100,
                ProductId = 100,
                CurrentStock = 0, // Zero stock
                ReorderPoint = 10,
                Price = 100,
                Cost = 50,
                UpdatedStock = DateTime.UtcNow.AddDays(-60) // Old but zero stock
            };
            _context.ProductCategories.Add(category);
            await _context.SaveChangesAsync();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var deadStockFindings = result.Findings.Where(f => f.Title.Contains("Dead Stock")).ToList();
            Assert.DoesNotContain(deadStockFindings, f => f.EntityId == "100");
        }

        // ========================================
        // DETECTOR 3: Margin Health Tests
        // ========================================

        [Fact]
        public async Task DetectMarginHealth_ReturnsFindings_WhenMarginBelow20Percent()
        {
            // Arrange
            SeedLowMarginData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var marginFindings = result.Findings.Where(f => f.Title.Contains("Margin") || f.Title.Contains("Money")).ToList();
            Assert.Equal(2, marginFindings.Count);

            var losingMoney = marginFindings.FirstOrDefault(f => f.Severity == "critical");
            Assert.NotNull(losingMoney);
            Assert.Equal("Losing Money", losingMoney.Title);
            Assert.True(losingMoney.MetricValue < 0);

            var lowMargin = marginFindings.FirstOrDefault(f => f.Severity == "warning");
            Assert.NotNull(lowMargin);
            Assert.Equal("Low Profit Margin", lowMargin.Title);
            Assert.True(lowMargin.MetricValue < 20);
        }

        [Fact]
        public async Task DetectMarginHealth_ReturnsEmpty_WhenMarginHealthy()
        {
            // Arrange
            SeedHealthyMarginData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var marginFindings = result.Findings.Where(f => f.Title.Contains("Margin")).ToList();
            Assert.Empty(marginFindings);
        }

        // ========================================
        // DETECTOR 4: Budget Burn Rate Tests
        // ========================================

        [Fact]
        public async Task DetectBudgetBurnRate_ReturnsWarning_WhenOver80Percent()
        {
            // Arrange
            SeedBudgetBurnData(budgetAmount: 10000m, expensesAmount: 8500m);

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var budgetFindings = result.Findings.Where(f => f.Title.Contains("Budget")).ToList();
            Assert.Single(budgetFindings);

            var finding = budgetFindings.First();
            Assert.Equal("finance", finding.Category);
            Assert.Equal("warning", finding.Severity);
            Assert.Equal("Budget Alert", finding.Title);
            Assert.True(finding.MetricValue >= 80);
        }

        [Fact]
        public async Task DetectBudgetBurnRate_ReturnsCritical_WhenBudgetExceeded()
        {
            // Arrange
            SeedBudgetBurnData(budgetAmount: 10000m, expensesAmount: 12000m);

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var budgetFindings = result.Findings.Where(f => f.Title.Contains("Budget")).ToList();
            Assert.Single(budgetFindings);

            var finding = budgetFindings.First();
            Assert.Equal("critical", finding.Severity);
            Assert.Equal("Budget Exceeded", finding.Title);
            Assert.True(finding.MetricValue >= 100);
        }

        [Fact]
        public async Task DetectBudgetBurnRate_ReturnsEmpty_WhenNoBudgetSet()
        {
            // Arrange - no budget data

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var budgetFindings = result.Findings.Where(f => f.Title.Contains("Budget")).ToList();
            Assert.Empty(budgetFindings);
        }

        [Fact]
        public async Task DetectBudgetBurnRate_ReturnsEmpty_WhenUnder80Percent()
        {
            // Arrange
            SeedBudgetBurnData(budgetAmount: 10000m, expensesAmount: 5000m);

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            var budgetFindings = result.Findings.Where(f => f.Title.Contains("Budget") && !f.Title.Contains("Category")).ToList();
            Assert.Empty(budgetFindings);
        }

        // ========================================
        // DETECTOR 5: Over-Budget Categories Tests
        // ========================================
        // NOTE: Detector 5 is currently disabled due to schema limitations.
        // BudgetHistory doesn't have category allocations (AllocatedAmount, CategoryId).
        // Tests will pass but detector returns empty results.

        [Fact]
        public async Task DetectOverBudgetCategories_ReturnsEmpty_SchemaNotReady()
        {
            // Arrange
            SeedOverBudgetCategoryData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert - detector should return empty until schema is extended
            var categoryFindings = result.Findings.Where(f => f.Title.Contains("Category Over Budget")).ToList();
            Assert.Empty(categoryFindings);
        }

        // ========================================
        // Integration Tests
        // ========================================

        [Fact]
        public async Task ScanAllAsync_ExecutesAllDetectors()
        {
            // Arrange
            SeedComprehensiveTestData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(5, result.DetectorsExecuted);
            Assert.Equal(0, result.DetectorsFailed);
            Assert.True(result.Findings.Count > 0);
            Assert.NotNull(result.ScanCompletedAt);
            Assert.True(result.DurationMs > 0);
        }

        [Fact]
        public async Task ScanAllAsync_ReturnsEmptyFindings_ForHealthyBusiness()
        {
            // Arrange
            SeedHealthyBusinessData();

            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            Assert.True(result.Success);
            Assert.Empty(result.Findings);
        }

        [Fact]
        public async Task ScanAllAsync_IncludesDetectorVersion()
        {
            // Act
            var result = await _scanner.ScanAllAsync(TEST_BUSINESS_ID);

            // Assert
            Assert.Equal("v1.0.0", result.DetectorVersion);
        }

        // ========================================
        // Helper Methods: Test Data Seeding
        // ========================================

        private void SeedStockoutRiskData()
        {
            var products = new[]
            {
                new Product { ProductId = 1, ProductName = "Widget A", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow },
                new Product { ProductId = 2, ProductName = "Widget B", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow }
            };
            _context.Products.AddRange(products);

            var categories = new[]
            {
                new ProductCategory { ProductCategoryId = 1, ProductId = 1, CurrentStock = 0, ReorderPoint = 10, Price = 100, Cost = 50 },
                new ProductCategory { ProductCategoryId = 2, ProductId = 2, CurrentStock = 3, ReorderPoint = 5, Price = 100, Cost = 50 }
            };
            _context.ProductCategories.AddRange(categories);
            _context.SaveChanges();
        }

        private void SeedHealthyStockData()
        {
            var product = new Product { ProductId = 10, ProductName = "Healthy Stock", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow };
            _context.Products.Add(product);

            var category = new ProductCategory
            {
                ProductCategoryId = 10,
                ProductId = 10,
                CurrentStock = 50,
                ReorderPoint = 10,
                Price = 100,
                Cost = 50
            };
            _context.ProductCategories.Add(category);
            _context.SaveChanges();
        }

        private void SeedOtherBusinessData(int businessId)
        {
            var product = new Product { ProductId = 999, ProductName = "Other Business Product", BusinessId = businessId, CreatedAt = DateTime.UtcNow };
            _context.Products.Add(product);

            var category = new ProductCategory
            {
                ProductCategoryId = 999,
                ProductId = 999,
                CurrentStock = 0,
                ReorderPoint = 10,
                Price = 100,
                Cost = 50
            };
            _context.ProductCategories.Add(category);
            _context.SaveChanges();
        }

        private void SeedDeadStockData()
        {
            var products = new[]
            {
                new Product { ProductId = 20, ProductName = "Old Product A", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow },
                new Product { ProductId = 21, ProductName = "Old Product B", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow }
            };
            _context.Products.AddRange(products);

            var categories = new[]
            {
                new ProductCategory
                {
                    ProductCategoryId = 20,
                    ProductId = 20,
                    CurrentStock = 25,
                    ReorderPoint = 5,
                    Price = 100,
                    Cost = 50,
                    UpdatedStock = DateTime.UtcNow.AddDays(-45)
                },
                new ProductCategory
                {
                    ProductCategoryId = 21,
                    ProductId = 21,
                    CurrentStock = 10,
                    ReorderPoint = 5,
                    Price = 100,
                    Cost = 50,
                    UpdatedStock = DateTime.UtcNow.AddDays(-60)
                }
            };
            _context.ProductCategories.AddRange(categories);
            _context.SaveChanges();
        }

        private void SeedLowMarginData()
        {
            var products = new[]
            {
                new Product { ProductId = 30, ProductName = "Losing Product", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow },
                new Product { ProductId = 31, ProductName = "Low Margin Product", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow }
            };
            _context.Products.AddRange(products);

            var categories = new[]
            {
                new ProductCategory
                {
                    ProductCategoryId = 30,
                    ProductId = 30,
                    CurrentStock = 10,
                    ReorderPoint = 5,
                    Price = 80, // Selling below cost
                    Cost = 100
                },
                new ProductCategory
                {
                    ProductCategoryId = 31,
                    ProductId = 31,
                    CurrentStock = 10,
                    ReorderPoint = 5,
                    Price = 105, // 4.8% margin (below 20%)
                    Cost = 100
                }
            };
            _context.ProductCategories.AddRange(categories);
            _context.SaveChanges();
        }

        private void SeedHealthyMarginData()
        {
            var product = new Product { ProductId = 40, ProductName = "Healthy Margin Product", BusinessId = TEST_BUSINESS_ID, CreatedAt = DateTime.UtcNow };
            _context.Products.Add(product);

            var category = new ProductCategory
            {
                ProductCategoryId = 40,
                ProductId = 40,
                CurrentStock = 10,
                ReorderPoint = 5,
                Price = 150, // 33% margin
                Cost = 100
            };
            _context.ProductCategories.Add(category);
            _context.SaveChanges();
        }

        private void SeedBudgetBurnData(decimal budgetAmount, decimal expensesAmount)
        {
            var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);

            var budget = new Budget
            {
                BudgetId = 1,
                BusinessId = TEST_BUSINESS_ID,
                MonthYear = currentMonth,
                MonthlyBudgetAmount = budgetAmount,
                CreatedAt = DateTime.UtcNow
            };
            _context.Budgets.Add(budget);

            // Create expenses totaling the specified amount
            var expense = new Expense
            {
                Id = Guid.NewGuid(),
                BusinessId = TEST_BUSINESS_ID,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = expensesAmount,
                Status = "paid",
                CreatedAt = DateTime.UtcNow
            };
            _context.Expenses.Add(expense);
            _context.SaveChanges();
        }

        private void SeedOverBudgetCategoryData()
        {
            // NOTE: This data won't trigger findings since schema doesn't support category allocations
            var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);

            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Utilities",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Categories.Add(category);

            var budget = new Budget
            {
                BudgetId = 2,
                BusinessId = TEST_BUSINESS_ID,
                MonthYear = currentMonth,
                MonthlyBudgetAmount = 10000m,
                CreatedAt = DateTime.UtcNow
            };
            _context.Budgets.Add(budget);

            var expense = new Expense
            {
                Id = Guid.NewGuid(),
                BusinessId = TEST_BUSINESS_ID,
                CategoryId = category.Id,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = 1500m,
                Status = "paid",
                CreatedAt = DateTime.UtcNow
            };
            _context.Expenses.Add(expense);
            _context.SaveChanges();
        }

        private void SeedHealthyBudgetCategoryData()
        {
            // NOTE: This data won't trigger findings since schema doesn't support category allocations
            var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);

            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Office Supplies",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Categories.Add(category);

            var budget = new Budget
            {
                BudgetId = 3,
                BusinessId = TEST_BUSINESS_ID,
                MonthYear = currentMonth,
                MonthlyBudgetAmount = 10000m,
                CreatedAt = DateTime.UtcNow
            };
            _context.Budgets.Add(budget);

            var expense = new Expense
            {
                Id = Guid.NewGuid(),
                BusinessId = TEST_BUSINESS_ID,
                CategoryId = category.Id,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = 500m,
                Status = "paid",
                CreatedAt = DateTime.UtcNow
            };
            _context.Expenses.Add(expense);
            _context.SaveChanges();
        }

        private void SeedComprehensiveTestData()
        {
            SeedStockoutRiskData();
            SeedDeadStockData();
            SeedLowMarginData();
            SeedBudgetBurnData(10000m, 9000m);
        }

        private void SeedHealthyBusinessData()
        {
            SeedHealthyStockData();
            SeedHealthyMarginData();
            SeedBudgetBurnData(10000m, 3000m);
        }
    }
}
