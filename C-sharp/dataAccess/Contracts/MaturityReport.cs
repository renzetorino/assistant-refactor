/*
============================================================================
COMMENTED OUT: Business Maturity Analytics - Domain Contracts
============================================================================
REASON: The Business Maturity feature did not meet team and advisor standards.

KEY ISSUES IDENTIFIED:
- Data pollution: Writes to ai_insights table with category="maturity", conflicting with mentorship insights
- Placeholder logic: Uses hardcoded values instead of real ActivityLog data
- Unreliable results: Default score of 50 for small datasets, not decision-ready
- Incomplete implementation: See MaturityScoreCalculator.cs comments for technical details

REFERENCE: See ai-insights-flaws-analysis.md Flaw #7, #8, #9

FUTURE UPGRADE PLAN:
- Implement dedicated maturity_reports table (separate from ai_insights)
- Integrate real ActivityLog data for Agility scoring
- Calculate Trust score from forecast adherence (not inventory updates)
- Add caching layer to prevent redundant calculations

DATE COMMENTED: January 15, 2026
============================================================================
*/
using System;
using System.Collections.Generic;

namespace dataAccess.Contracts
{
    /// <summary>
    /// Result of maturity score calculation for a business.
    /// </summary>
    /*
    public sealed class MaturityScoreResult
    {
        /// <summary>
        /// Business ID for which scores were calculated
        /// </summary>
        public int BusinessId { get; set; }

        /// <summary>
        /// Agility Score (0-100): Measures response time to stockout alerts
        /// </summary>
        public decimal AgilityScore { get; set; }

        /// <summary>
        /// Discipline Score (0-100): Measures bookkeeping lag (time between occurrence and entry)
        /// </summary>
        public decimal DisciplineScore { get; set; }

        /// <summary>
        /// Trust Score (0-100): Measures adherence to AI forecasts
        /// </summary>
        public decimal TrustScore { get; set; }

        /// <summary>
        /// Overall Operational Health Score (0-100): Weighted average of all scores
        /// </summary>
        public decimal HealthScore { get; set; }

        /// <summary>
        /// Time period analyzed
        /// </summary>
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Raw metrics used for score calculation
        /// </summary>
        public MaturityMetrics Metrics { get; set; } = new();

        /// <summary>
        /// Success flag
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if calculation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Raw metrics used to calculate maturity scores
    /// </summary>
    public sealed class MaturityMetrics
    {
        /// <summary>
        /// Average time (in hours) to respond to low stock alerts
        /// </summary>
        public decimal? AvgResponseTimeHours { get; set; }

        /// <summary>
        /// Average bookkeeping lag (in days) between expense occurrence and entry
        /// </summary>
        public decimal? AvgBookkeepingLagDays { get; set; }

        /// <summary>
        /// Percentage of AI forecast adherence (0-100)
        /// </summary>
        public decimal? ForecastAdherencePercent { get; set; }

        /// <summary>
        /// Number of stockout alerts in period
        /// </summary>
        public int StockoutAlertCount { get; set; }

        /// <summary>
        /// Number of expenses recorded in period
        /// </summary>
        public int ExpenseCount { get; set; }

        /// <summary>
        /// Number of forecasts available for comparison
        /// </summary>
        public int ForecastComparisonCount { get; set; }
    }

    /// <summary>
    /// Monthly maturity trend data
    /// </summary>
    public sealed class MonthlyMaturityTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal AgilityScore { get; set; }
        public decimal DisciplineScore { get; set; }
        public decimal TrustScore { get; set; }
        public decimal HealthScore { get; set; }
        public MaturityMetrics Metrics { get; set; } = new();
    }

    /// <summary>
    /// Complete maturity report with trends
    /// </summary>
    public sealed class MaturityReport
    {
        public int BusinessId { get; set; }
        public MaturityScoreResult CurrentScores { get; set; } = new();
        public List<MonthlyMaturityTrend> MonthlyTrends { get; set; } = new();
        public string MaturityLevel { get; set; } = "Novice";
        public DateTime GeneratedAt { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
    */
}
