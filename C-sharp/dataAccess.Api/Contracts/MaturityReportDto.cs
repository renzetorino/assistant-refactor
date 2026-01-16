/*
============================================================================
COMMENTED OUT: Business Maturity Analytics - API DTOs
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
using System.Text.Json.Serialization;

namespace dataAccess.Api.Contracts
{
    /// <summary>
    /// DTO for maturity report response.
    /// </summary>
    /*
    public sealed class MaturityReportDto
    {
        [JsonPropertyName("businessId")]
        public int BusinessId { get; set; }

        [JsonPropertyName("currentScores")]
        public MaturityScoresDto CurrentScores { get; set; } = new();

        [JsonPropertyName("monthlyTrends")]
        public List<MonthlyTrendDto> MonthlyTrends { get; set; } = new();

        [JsonPropertyName("maturityLevel")]
        public string MaturityLevel { get; set; } = string.Empty;

        [JsonPropertyName("generatedAt")]
        public DateTime GeneratedAt { get; set; }
    }

    public sealed class MaturityScoresDto
    {
        [JsonPropertyName("agilityScore")]
        public decimal AgilityScore { get; set; }

        [JsonPropertyName("disciplineScore")]
        public decimal DisciplineScore { get; set; }

        [JsonPropertyName("trustScore")]
        public decimal TrustScore { get; set; }

        [JsonPropertyName("healthScore")]
        public decimal HealthScore { get; set; }

        [JsonPropertyName("metrics")]
        public MetricsDto Metrics { get; set; } = new();
    }

    public sealed class MetricsDto
    {
        [JsonPropertyName("avgResponseTimeHours")]
        public decimal? AvgResponseTimeHours { get; set; }

        [JsonPropertyName("avgBookkeepingLagDays")]
        public decimal? AvgBookkeepingLagDays { get; set; }

        [JsonPropertyName("forecastAdherencePercent")]
        public decimal? ForecastAdherencePercent { get; set; }

        [JsonPropertyName("stockoutAlertCount")]
        public int StockoutAlertCount { get; set; }

        [JsonPropertyName("expenseCount")]
        public int ExpenseCount { get; set; }

        [JsonPropertyName("forecastComparisonCount")]
        public int ForecastComparisonCount { get; set; }
    }

    public sealed class MonthlyTrendDto
    {
        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("month")]
        public int Month { get; set; }

        [JsonPropertyName("agilityScore")]
        public decimal AgilityScore { get; set; }

        [JsonPropertyName("disciplineScore")]
        public decimal DisciplineScore { get; set; }

        [JsonPropertyName("trustScore")]
        public decimal TrustScore { get; set; }

        [JsonPropertyName("healthScore")]
        public decimal HealthScore { get; set; }
    }
    */
}
