using System;

namespace dataAccess.Contracts
{
    /// <summary>
    /// Represents a critical business issue for the morning briefing.
    /// </summary>
    public sealed class CriticalIssue
    {
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty; // "critical", "warning", "info"
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? EntityName { get; set; }
        public decimal? MetricValue { get; set; }
        public int Priority { get; set; } // 1=highest
    }

    /// <summary>
    /// Product inventory risk analysis data.
    /// </summary>
    public sealed class ProductRiskData
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public decimal? ReorderPoint { get; set; }
        public decimal? AverageDailySales { get; set; }
        public int? DaysUntilStockout { get; set; }
        public decimal Price { get; set; }
        public decimal? PotentialLostRevenue { get; set; }
        public int? DaysSinceLastSale { get; set; }
        public string RiskType { get; set; } = string.Empty; // "stockout" or "dead_stock"
    }

    /// <summary>
    /// Profit margin analysis by category.
    /// </summary>
    public sealed class MarginData
    {
        public string Category { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Profit { get; set; }
        public decimal ProfitMarginPercent { get; set; }
        public int TransactionCount { get; set; }
    }

    /// <summary>
    /// Monthly expense trend with seasonality detection.
    /// </summary>
    public sealed class SpendingTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string Category { get; set; } = string.Empty;
        public decimal TotalSpent { get; set; }
        public decimal AverageMonthlySpend { get; set; }
        public decimal PercentDeviation { get; set; }
        public bool IsAnomaly { get; set; }
        public string AnomalyType { get; set; } = string.Empty; // "spike", "drop", "normal"
    }
}
